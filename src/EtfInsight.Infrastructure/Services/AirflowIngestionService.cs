using System.Data;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using EtfInsight.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EtfInsight.Infrastructure.Services;

public class AirflowIngestionService(
    IDbConnection db,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<AirflowIngestionService> logger) : IIngestionService
{
    private readonly string _airflowBase =
        config["Airflow:BaseUrl"] ?? "http://localhost:8090";
    private readonly string _credentials = Convert.ToBase64String(
        Encoding.UTF8.GetBytes(
            $"{config["Airflow:Username"] ?? "airflow"}:{config["Airflow:Password"] ?? "airflow"}"));

    public async Task<IngestionStatus> EnsureTickerReadyAsync(
        string ticker, CancellationToken ct = default)
    {
        ticker = ticker.Trim().ToUpperInvariant();

        // 1. Check current status — no DAG trigger needed if already ready/ingesting
        var current = await db.QueryFirstOrDefaultAsync<string>(
            "SELECT status FROM etf_metadata WHERE ticker = @Ticker",
            new { Ticker = ticker });

        if (current == "ready")    return IngestionStatus.Ready;
        if (current is "pending" or "ingesting") return IngestionStatus.Ingesting;

        // 2. Insert placeholder row so the FK on transactions is satisfied immediately
        await db.ExecuteAsync("""
            INSERT INTO etf_metadata (ticker, name, status, is_active, ingestion_requested_at)
            VALUES (@Ticker, @Ticker, 'pending'::etf_ingestion_status, false, NOW())
            ON CONFLICT (ticker) DO UPDATE
                SET status = 'pending'::etf_ingestion_status,
                    ingestion_requested_at = NOW()
                WHERE etf_metadata.status = 'unknown'::etf_ingestion_status
                   OR etf_metadata.status = 'error'::etf_ingestion_status;
            """,
            new { Ticker = ticker });

        // 3. Trigger Airflow DAG via REST API
        var dagRunId =
            $"jit_{ticker.ToLowerInvariant().Replace(".", "_")}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var payload = new
        {
            dag_run_id = dagRunId,
            conf = new
            {
                ticker,
                date_from = "2015-01-01",
                date_to = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            }
        };

        var http = httpClientFactory.CreateClient("Airflow");
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_airflowBase}/api/v1/dags/etf_backfill_jit/dagRuns")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Authorization = new("Basic", _credentials);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HTTP call to Airflow failed for ticker {Ticker}", ticker);
            await MarkErrorAsync(ticker, ex.Message);
            return IngestionStatus.Error;
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Airflow returned {Status} for ticker {Ticker}: {Body}",
                (int)resp.StatusCode, ticker, body);
            await MarkErrorAsync(ticker, body);
            return IngestionStatus.Error;
        }

        // 4. Mark as ingesting
        await db.ExecuteAsync(
            "UPDATE etf_metadata SET status = 'ingesting'::etf_ingestion_status WHERE ticker = @Ticker",
            new { Ticker = ticker });

        logger.LogInformation(
            "JIT DAG triggered for {Ticker}, runId={RunId}", ticker, dagRunId);

        return IngestionStatus.Ingesting;
    }

    private Task MarkErrorAsync(string ticker, string error) =>
        db.ExecuteAsync(
            """
            UPDATE etf_metadata
            SET status = 'error'::etf_ingestion_status, ingestion_error = @Error
            WHERE ticker = @Ticker
            """,
            new { Ticker = ticker, Error = error });
}
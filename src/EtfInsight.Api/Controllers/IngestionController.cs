using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace EtfInsight.Api.Controllers
{
    [ApiController]
    [Route("api/ingestion")]
    [Produces("application/json")]
    public class IngestionController(
        IDbConnection db,
        IConfiguration config,
        ILogger<IngestionController> logger) : ControllerBase
    {
        private readonly string _callbackSecret = config["Airflow:CallbackSecret"] ?? string.Empty;



        /// <summary>
        /// Called by Airflow when a JIT DAG run completes or fails.
        /// Airflow already updated etf_metadata directly; this is belt-and-suspenders
        /// and the authoritative signal the frontend polls against.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("callback")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Callback([FromBody] IngestionCallbackRequest request)
        {
            // Validate shared secret to prevent spoofing
            if (!string.IsNullOrEmpty(_callbackSecret))
            {
                if (!Request.Headers.TryGetValue("X-Callback-Secret", out var incoming)
                    || incoming != _callbackSecret)
                {
                    logger.LogWarning(
                        "Rejected ingestion callback for {Ticker} — invalid secret", request.Ticker);
                    return Unauthorized(new { Error = "Invalid callback secret." });
                }
            }

            var validStatuses = new[] { "ready", "error", "ingesting", "pending" };
            if (string.IsNullOrWhiteSpace(request.Ticker))
            {
                return BadRequest(new { Error = "Ticker is required." });
            }

            if (!validStatuses.Contains(request.Status?.ToLower()))
            {
                return BadRequest(new { Error = $"Status must be one of: {string.Join(", ", validStatuses)}" });
            }

            var status = request.Status!.ToLower();

            logger.LogInformation(
                "Ingestion callback received: ticker={Ticker} status={Status} dagRunId={RunId}",
                request.Ticker, status, request.DagRunId);

            await db.ExecuteAsync("""
            UPDATE etf_metadata
            SET status                 = @Status::etf_ingestion_status,
                is_active              = (@Status = 'ready'),
                ingestion_completed_at = CASE WHEN @Status = 'ready' THEN NOW()
                                              ELSE ingestion_completed_at END,
                ingestion_error        = @Error
            WHERE ticker = @Ticker
            """,
            new
            {
                Ticker = request.Ticker.Trim().ToUpperInvariant(),
                Status = status,
                Error = request.Error
            });

            return Ok(new { acknowledged = true, ticker = request.Ticker, status });
        }


        /// <summary>
        /// Polled by the frontend every few seconds to check ingestion progress.
        /// Returns 404 if the ticker has never been seen.
        /// </summary>
        [HttpGet("{ticker}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus(string ticker)
        {
            var row = await db.QueryFirstOrDefaultAsync("""
            SELECT
                ticker,
                status,
                ingestion_requested_at   AS requestedAt,
                ingestion_completed_at   AS completedAt,
                ingestion_error          AS error
            FROM etf_metadata
            WHERE ticker = @Ticker
            """,
                new { Ticker = ticker.Trim().ToUpperInvariant() });

            return row is null
                ? NotFound(new { Error = $"Ticker '{ticker}' not found." })
                : Ok(row);
        }
    }
}

public record IngestionCallbackRequest(
    string Ticker,
    string Status, // "ready" |"error"
    string? Error = null,
    string? DagRunId = null
);
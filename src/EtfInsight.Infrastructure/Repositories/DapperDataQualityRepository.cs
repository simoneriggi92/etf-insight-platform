using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EtfInsight.DataQuality.Entities;
using EtfInsight.DataQuality.Interfaces;
using EtfInsight.DataQuality.DTOs;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;

namespace EtfInsight.Infrastructure.Repositories
{
    public class DapperDataQualityRepository : IDataQualityRepository
    {
        private readonly IDbConnection _db;

        public DapperDataQualityRepository(IDbConnection db)
        {
            _db = db;
        }

        public async Task<IEnumerable<DataAnomaly>> GetAnomaliesByTickerAsync(string ticker, int days = 30)
        {
            var query = @"
                SELECT 
                    id as Id,
                    ticker as Ticker,
                    price_date as PriceDate,
                    rule_name as RuleName,
                    severity as Severity,
                    current_value as CurrentValue,
                    expected_range as ExpectedRange,
                    message as Message,
                    metadata as Metadata,
                    detected_at as DetectedAt,
                    resolved as Resolved,
                    resolved_at as ResolvedAt,
                    resolved_by as ResolvedBy
                FROM data_anomalies
                WHERE ticker = @Ticker 
                    AND detected_at >= @FromDate
                ORDER BY detected_at DESC";

            var anomalies = await _db.QueryAsync<DataAnomaly>(query, new
            {
                Ticker = ticker,
                FromDate = DateTime.UtcNow.AddDays(-days)
            });

            return anomalies.ToList();
        }

        public async Task<IEnumerable<DataAnomaly>> GetUnresolvedAnomaliesAsync()
        {
            var query = @"
                SELECT 
                    id as Id,
                    ticker as Ticker,
                    price_date as PriceDate,
                    rule_name as RuleName,
                    severity as Severity,
                    current_value as CurrentValue,
                    expected_range as ExpectedRange,
                    message as Message,
                    metadata as Metadata,
                    detected_at as DetectedAt,
                    resolved as Resolved,
                    resolved_at as ResolvedAt,
                    resolved_by as ResolvedBy
                FROM data_anomalies
                WHERE resolved = FALSE
                ORDER BY detected_at DESC";

            var anomalies = await _db.QueryAsync<DataAnomaly>(query);

            return anomalies.ToList();
        }

        public async Task InsertAnomalyAsync(DataAnomaly anomaly)
        {
            var query = @"
                INSERT INTO data_anomalies (
                    id, ticker, price_date, rule_name, severity, 
                    current_value, expected_range, message, 
                    metadata, detected_at, resolved
                )
                VALUES (
                    @Id, @Ticker, @PriceDate, @RuleName, @Severity,
                    @CurrentValue, @ExpectedRange, @Message,
                    @Metadata::jsonb, @DetectedAt, @Resolved
                )";

            await _db.ExecuteAsync(query, new
            {
                anomaly.Id,
                anomaly.Ticker,
                PriceDate = anomaly.PriceDate.ToDateTime(TimeOnly.MinValue),
                anomaly.RuleName,
                anomaly.Severity,
                anomaly.CurrentValue,
                anomaly.ExpectedRange,
                anomaly.Message,
                anomaly.Metadata,
                anomaly.DetectedAt,
                anomaly.Resolved
            });
        }
    }
}
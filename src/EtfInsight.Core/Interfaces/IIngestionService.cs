using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EtfInsight.Core.Interfaces
{
    public interface IIngestionService
    {
        /// <summary>
        /// Ensures the ticker in etf_metadata and triggers a JIT DAG run if needed
        /// </summary>
        /// <param name="ticker">The ETF ticker to ingest, e.g. VUSA.MI or SWDA.MI</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>The ingestion status</returns>
        Task<IngestionStatus> EnsureTickerReadyAsync(string ticker, CancellationToken ct = default);
    }

    public enum IngestionStatus
    {
        Ready, // prices already in DB
        Ingesting, // DAG triggered or already running
        Error // Airflow call failed
    }
}
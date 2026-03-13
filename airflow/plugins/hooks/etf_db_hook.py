from __future__ import annotations
from airflow.providers.postgres.hooks.postgres import PostgresHook


class ETFDatabaseHook(PostgresHook):
    """
    PostgresHook wrapper with ETF-specific helpers.
    Mirrors DB logic from src/ingestion/load_to_db.py and
    src/ingestion/ingest_prices_yfinance.py.
    """

    conn_name_attr = "etf_postgres"
    default_conn_name = "etf_postgres"

    def get_active_symbols(self) -> list[str]:
        """
        Mirrors get_active_etf_symbols() in ingest_prices_yfinance.py.
        Returns tickers where is_active = TRUE, sorted alphabetically.
        """
        rows = self.get_records(
            "SELECT ticker FROM etf_metadata WHERE is_active = TRUE ORDER BY ticker"
        )
        return [r[0] for r in rows]

    def upsert_prices(self, records: list[dict]) -> int:
        """
        Mirrors insert_prices() in load_to_db.py.
        Same ON CONFLICT (symbol, price_date) DO UPDATE logic.
        Returns number of rows affected.
        """
        if not records:
            return 0

        sql = """
            INSERT INTO etf_prices
                (ticker, price_date, open_price, high_price, low_price,
                 close_price, volume)
            VALUES
                (%(ticker)s, %(price_date)s, %(open_price)s, %(high_price)s,
                 %(low_price)s, %(close_price)s, %(volume)s)
            ON CONFLICT (ticker, price_date)
            DO UPDATE SET
                open_price  = EXCLUDED.open_price,
                high_price  = EXCLUDED.high_price,
                low_price   = EXCLUDED.low_price,
                close_price = EXCLUDED.close_price,
                volume      = EXCLUDED.volume,
                created_at  = now();
        """
        conn = self.get_conn()
        cur = conn.cursor()
        cur.executemany(sql, records)
        affected = cur.rowcount
        conn.commit()
        cur.close()
        return affected

    def upsert_metadata(
        self, ticker: str, status: str, ingestion_error: str | None = None
    ) -> None:
        """
        Insert a placeholder row for a new ticker, or update the status of an existing one. Called by etf_backfill_jit at completion/failure.
        """

        sql = """
            INSERT INTO etf_metadata (ticker, name, status, is_active, ingestion_requested_at)
            VALUES (%(ticker)s, %(name)s, %(status)s::etf_ingestion_status, FALSE, NOW())
            ON CONFLICT (ticker) DO UPDATE
                SET status                 = EXCLUDED.status::etf_ingestion_status,
                    is_active              = (%(status)s = 'ready'),
                    ingestion_completed_at = CASE
                        WHEN %(status)s = 'ready' THEN NOW()
                        ELSE etf_metadata.ingestion_completed_at
                    END,
                    ingestion_error        = %(ingestion_error)s;
        """
        conn = self.get_conn()
        cur = conn.cursor()
        cur.execute(
            sql,
            {
                "ticker": ticker,
                "name": ticker,  # placeholder; can be enriched later
                "status": status,
                "ingestion_error": ingestion_error,
            },
        )
        conn.commit()
        cur.close()

    def get_ticker_status(self, ticker: str) -> str | None:
        """
        Returns the current ingestion status for a ticker, or None if not found.
        Useful for DAG-side guard checks before triggering redundant runs.
        """
        rows = self.get_records(
            "SELECT status FROM etf_metadata WHERE ticker = %s",
            parameters=[ticker],
        )
        return rows[0][0] if rows else None

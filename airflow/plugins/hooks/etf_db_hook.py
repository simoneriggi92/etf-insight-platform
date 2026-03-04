from __future__ import annotations
from airflow.providers.postgres.hooks.postgres import PostgresHook


class ETFDatabaseHook(PostgresHook):
    """
    PostgresHook wrapper with ETF-specific helpers.
    Mirrors DB logic from src/ingestion/load_to_db.py and
    src/ingestion/ingest_prices_yfinance.py.
    """

    conn_name_attr = "etf_postgres_conn_id"
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
                (symbol, price_date, open_price, high_price, low_price,
                 close_price, volume)
            VALUES
                (%(symbol)s, %(price_date)s, %(open)s, %(high)s, %(low)s,
                 %(close)s, %(volume)s)
            ON CONFLICT (symbol, price_date)
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

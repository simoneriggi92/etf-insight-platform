from __future__ import annotations
import pandas as pd
import yfinance as yf


def fetch_raw_prices(symbol: str, period: str = "5d") -> list[dict]:
    """
    Extract — scheduled mode.
    Mirrors the scheduled branch of fetch_etf_price() in ingest_prices_yfinance.py.
    """
    df = yf.Ticker(symbol).history(period=period)
    if df.empty:
        return []
    df.index = pd.to_datetime(df.index).date
    df.reset_index(inplace=True)
    return df.to_dict(orient="records")


def fetch_raw_prices_range(symbol: str, start: str, end: str) -> list[dict]:
    """
    Extract — backfill mode.
    Replaces manual --from/--to CLI args of ingest_prices_yfinance.py.
    """
    df = yf.Ticker(symbol).history(start=start, end=end)
    if df.empty:
        return []
    df.index = pd.to_datetime(df.index).date
    df.reset_index(inplace=True)
    return df.to_dict(orient="records")


def normalize_prices(raw: list[dict], symbol: str) -> list[dict]:
    """
    Transform — replaces parse_price_file() in load_to_db.py.
    Casts types, renames columns, attaches symbol.
    Skips malformed rows silently (Airflow task log will show count).
    """
    result = []
    for row in raw:
        price_date = row.get("Date") or row.get("price_date")
        if isinstance(price_date, pd.Timestamp):
            price_date = price_date.date()
        try:
            result.append(
                {
                    "symbol": symbol,
                    "price_date": str(price_date),
                    "open": float(row.get("Open", 0)),
                    "high": float(row.get("High", 0)),
                    "low": float(row.get("Low", 0)),
                    "close": float(row.get("Close", 0)),
                    "volume": int(row.get("Volume", 0)),
                }
            )
        except (TypeError, ValueError) as e:
            print(f"[normalize_prices] Skipping malformed row for {symbol}: {e}")
    return result


def validate_prices(records: list[dict]) -> list[dict]:
    """
    Transform — sanity checks before upsert.
    Mirrors the implicit guards in insert_prices() in load_to_db.py.
    """
    valid, dropped = [], 0
    for r in records:
        if r["close"] <= 0 or r["high"] < r["low"] or not r["price_date"]:
            dropped += 1
            continue
        valid.append(r)
    if dropped:
        print(f"[validate_prices] Dropped {dropped} invalid records.")
    return valid

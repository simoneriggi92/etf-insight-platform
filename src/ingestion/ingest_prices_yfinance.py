import psycopg2
import yfinance as yf
import json
import os
import time
from datetime import datetime
from pathlib import Path
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()

# Database connection config
DB_CONFIG = {
    "host": os.getenv("POSTGRES_HOST", "localhost"),
    "port": os.getenv("POSTGRES_PORT", "5432"),
    "database": os.getenv("POSTGRES_DB", "etfinsight"),
    "user": os.getenv("POSTGRES_USER", "etfinsight"),
    "password": os.getenv("POSTGRES_PASSWORD", "devpassword123"),
}


def get_db_connection():
    """Create database connection"""
    max_retries = 5
    retry_delay = 2  # seconds
    for attempt in range(max_retries):
        try:
            return psycopg2.connect(**DB_CONFIG)
        except Exception as e:
            if attempt < max_retries - 1:
                print(
                    f"Error connecting to database: {e}. Retrying in {retry_delay}s..."
                )
                time.sleep(retry_delay)
            else:
                raise Exception(
                    f"Failed to connect to database after {max_retries} attempts: {e}"
                )


def get_active_etf_symbols() -> list:
    """Retrieve active ETF symbols from the database"""
    conn = get_db_connection()
    cur = conn.cursor()
    cur.execute("SELECT symbol FROM etf_metadata WHERE is_active = TRUE;")
    rows = cur.fetchall()
    cur.close()
    conn.close()
    return [row[0] for row in rows]


def fetch_etf_price(symbol: str) -> dict:
    """Fetch historical prices using yfinance library"""
    print(f"Fetching {symbol}...")

    ticker = yf.Ticker(symbol)

    # Get last 5 days of data
    hist = ticker.history(period=os.getenv("PERIOD", "5d"))

    # Convert to dict format similar to Yahoo Finance API
    data = {"symbol": symbol, "data": hist.to_dict("index")}

    return data


def save_raw_response(symbol: str, data: dict):
    """Save raw API response to disk"""
    output_dir = Path("/app/data/raw")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Convert datetime keys to strings for JSON serialization
    if "data" in data:
        data["data"] = {str(k): v for k, v in data["data"].items()}

    filename = f"{symbol}_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    filepath = output_dir / filename

    with open(filepath, "w") as f:
        json.dump(data, f, indent=2, default=str)

    print(f"Saved to {filepath}")


def main():
    symbols = get_active_etf_symbols()

    for symbol in symbols:
        try:
            data = fetch_etf_price(symbol)
            save_raw_response(symbol, data)
            print(f"✓ {symbol} complete\n")
        except Exception as e:
            print(f"✗ {symbol} failed: {e}\n")


if __name__ == "__main__":
    print(f"Starting ingestion at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    main()
    print(f"Completed at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

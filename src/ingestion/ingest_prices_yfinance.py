import psycopg2
import yfinance as yf
import json
import os
import time
import schedule
import traceback
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

    # Check if data was retrieved
    if hist is None or hist.empty:
        raise ValueError(f"No data returned for {symbol}")

    # Convert to dict format similar to Yahoo Finance API
    data = {"symbol": symbol, "data": hist.to_dict("index")}

    return data


def save_raw_response(symbol: str, data: dict):
    """Save raw API response to disk with atomic write (outbox pattern)"""
    output_dir = Path("/app/data/raw")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Convert datetime keys to strings for JSON serialization
    if "data" in data:
        data["data"] = {str(k): v for k, v in data["data"].items()}

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    tmp_filename = f"{symbol}_{timestamp}.json.tmp"
    final_filename = f"{symbol}_{timestamp}.json"
    tmp_filepath = output_dir / tmp_filename
    final_filepath = output_dir / final_filename

    # Write to temp file first
    with open(tmp_filepath, "w") as f:
        json.dump(data, f, indent=2, default=str)

    # Atomic rename to final filename
    os.rename(tmp_filepath, final_filepath)

    print(f"Saved to {final_filepath}")


def cleanup_old_files(directory: Path, days_old: int = 7):
    """Delete files older than specified days in the given directory"""
    if not directory.exists():
        print(f"Directory {directory} does not exist, skipping cleanup")
        return

    now = time.time()
    cutoff = now - (days_old * 86400)  # days to seconds

    print(f"Cleaning up files older than {days_old} days in {directory}")
    deleted_count = 0

    for file in directory.iterdir():
        if file.is_file() and file.suffix in [".json", ".tmp"]:
            file_mtime = file.stat().st_mtime
            if file_mtime < cutoff:
                print(f"  Deleting old file: {file.name}")
                file.unlink()
                deleted_count += 1

    if deleted_count > 0:
        print(f"Deleted {deleted_count} old files")
    else:
        print(f"No old files to delete")


def scrape_job():
    """Main scraping job to run on schedule"""
    print(f"\n{'='*60}")
    print(f"Starting scrape job at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'='*60}")

    successful = 0
    failed = 0

    try:
        # Cleanup old files in raw directory
        raw_dir = Path("/app/data/raw")
        cleanup_old_files(raw_dir, days_old=7)
        print()

        symbols = get_active_etf_symbols()
        print(f"Found {len(symbols)} active ETF symbols: {', '.join(symbols)}\n")

        for symbol in symbols:
            try:
                data = fetch_etf_price(symbol)

                # Check if we got any data points
                data_points = len(data.get("data", {}))
                if data_points == 0:
                    print(f"  ⚠ {symbol}: No data points returned (market closed?)")
                    failed += 1
                    continue

                save_raw_response(symbol, data)
                print(f"  ✓ {symbol}: Saved {data_points} data points\n")
                successful += 1

            except Exception as e:
                failed += 1
                error_trace = traceback.format_exc()
                print(f"  ✗ {symbol} failed: {e}")
                print(f"  Stack trace:\n{error_trace}\n")

        print(f"Summary: {successful} successful, {failed} failed")
        print(f"Completed at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        print(f"{'='*60}\n")

    except Exception as e:
        error_trace = traceback.format_exc()
        print(f"✗ Scrape job failed: {e}")
        print(f"Stack trace:\n{error_trace}\n")


def main():
    print("ETF Price Scraper - Scheduled Mode")
    print(f"Schedule: Every {os.getenv('SCRAPER_INTERVAL_MINUTES', '2')} minutes")
    print(f"Started at: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")

    # Schedule the scraping job
    schedule.every(int(os.getenv("SCRAPER_INTERVAL_MINUTES", "2"))).minutes.do(
        scrape_job
    )

    # run immediately at startup
    scrape_job()

    # Keep running
    while True:
        schedule.run_pending()
        time.sleep(1)


if __name__ == "__main__":
    main()

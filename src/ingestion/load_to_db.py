import os
import sys
import traceback
import psycopg2
import json
from pathlib import Path
import time
from graceful_killer import GracefulKiller
import schedule
from datetime import datetime
from typing import List, Dict
from dotenv import load_dotenv

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
    """Create database connection with retry logic"""
    max_retries = 5
    retry_delay = 2

    for attempt in range(max_retries):
        try:
            return psycopg2.connect(**DB_CONFIG)
        except psycopg2.OperationalError as e:
            if attempt < max_retries - 1:
                print(
                    f"Connection attempt {attempt + 1} failed. Retrying in {retry_delay}s..."
                )
                time.sleep(retry_delay)
            else:
                raise Exception(f"Failed to connect after {max_retries} attempts: {e}")


def parse_price_file(filepath: Path) -> List[Dict]:
    """Parse saved price JSON and extract structured data"""
    with open(filepath) as f:
        data = json.load(f)

    symbol = data.get("symbol", "UNKNOWN")
    prices = data.get("data", {})

    parsed = []
    for date_str, values in prices.items():
        # Extract just date part (remove time if present)
        clean_date = (
            date_str.split()[0]
            if isinstance(date_str, str)
            else str(date_str).split()[0]
        )

        record = {
            "symbol": symbol,
            "price_date": clean_date,
            "open_price": values.get("Open"),
            "high_price": values.get("High"),
            "low_price": values.get("Low"),
            "close_price": values.get("Close"),
            "volume": int(values.get("Volume", 0)),
            "currency": values.get("Currency", "USD"),
        }
        parsed.append(record)

    return parsed


def insert_prices(records: List[Dict]) -> int:
    """Insert price records into database"""
    if not records:
        return 0

    conn = get_db_connection()
    cur = conn.cursor()

    insert_query = """
        INSERT INTO etf_prices (symbol, price_date, open_price, high_price, low_price, close_price, volume, currency)
        VALUES (%(symbol)s, %(price_date)s, %(open_price)s, %(high_price)s, %(low_price)s, %(close_price)s, %(volume)s, %(currency)s)
        ON CONFLICT (symbol, price_date) DO UPDATE SET
            open_price = EXCLUDED.open_price,
            high_price = EXCLUDED.high_price,
            low_price = EXCLUDED.low_price,
            close_price = EXCLUDED.close_price,
            volume = EXCLUDED.volume,
            currency = EXCLUDED.currency,
            created_at = NOW()
    """

    inserted = 0
    for record in records:
        try:
            cur.execute(insert_query, record)
            if cur.rowcount > 0:
                inserted += 1
        except Exception as e:
            print(f"  Error inserting {record['symbol']} {record['price_date']}: {e}")
            conn.rollback()
            continue

    conn.commit()
    cur.close()
    conn.close()

    return inserted


def move_file_to_processed(filepath: Path, processed_dir: Path):
    """Move successfully processed file to processed directory"""
    # Preserve subdirectory structure (e.g., history/)
    relative_to_raw = filepath.relative_to(
        filepath.parents[1] if filepath.parent.name == "history" else filepath.parent
    )
    dest_path = processed_dir / relative_to_raw
    dest_path.parent.mkdir(parents=True, exist_ok=True)
    filepath.rename(dest_path)
    print(f"  → Moved to {dest_path.relative_to(processed_dir.parent)}")


def move_file_to_error(filepath: Path, error_dir: Path, error_msg: str):
    """Move errored file to error directory with error log"""
    # Preserve subdirectory structure (e.g., history/)
    relative_to_raw = filepath.relative_to(
        filepath.parents[1] if filepath.parent.name == "history" else filepath.parent
    )
    dest_path = error_dir / relative_to_raw
    dest_path.parent.mkdir(parents=True, exist_ok=True)

    # Move the file
    filepath.rename(dest_path)

    # Create error log alongside
    error_log_path = dest_path.with_suffix(".error.log")
    with open(error_log_path, "w") as f:
        f.write(f"Error occurred at: {datetime.now().isoformat()}\n")
        f.write(f"File: {filepath.name}\n")
        f.write(f"\n{error_msg}\n")

    print(f"  → Moved to {dest_path.relative_to(error_dir.parent)}")
    print(f"  → Error log: {error_log_path.name}")


def ingest_job():
    """Main ingestion job to run on schedule"""
    print(f"\n{'='*60}")
    print(f"Starting ingest job at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'='*60}")

    raw_dir = Path("/app/data/raw")
    processed_dir = Path("/app/data/processed")
    error_dir = Path("/app/data/error")

    # Create processed and error directories
    processed_dir.mkdir(parents=True, exist_ok=True)
    error_dir.mkdir(parents=True, exist_ok=True)

    total_files = 0
    total_records = 0
    total_inserted = 0
    total_errors = 0

    # Process JSON files in raw/ and raw/history
    search_patterns = [
        raw_dir.glob("*.json"),
        raw_dir.glob("history/*.json"),
    ]

    all_files = []
    for pattern in search_patterns:
        all_files.extend(pattern)

    if not all_files:
        print("No files to process\n")
        print(f"{'='*60}\n")
        return

    # Process all JSON files
    for json_file in sorted(all_files):
        print(f"Processing {json_file.name}...")
        total_files += 1

        try:
            records = parse_price_file(json_file)
            total_records += len(records)

            inserted = insert_prices(records)
            total_inserted += inserted

            print(f"  Parsed {len(records)} records, inserted {inserted} new records")

            # Move to processed
            move_file_to_processed(json_file, processed_dir)
            print()

        except Exception as e:
            total_errors += 1
            error_trace = traceback.format_exc()
            print(f"  ✗ Error processing file: {e}")
            print(f"  Stack trace:\n{error_trace}")

            # Move to error directory with error log
            move_file_to_error(json_file, error_dir, error_trace)
            print()
            continue

    print(f"Summary:")
    print(f"  Files processed: {total_files}")
    print(f"  Successful: {total_files - total_errors}")
    print(f"  Failed: {total_errors}")
    print(f"  Total records parsed: {total_records}")
    print(f"  New records inserted: {total_inserted}")
    print(f"  Duplicates skipped: {total_records - total_inserted}")
    print(f"Completed at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'='*60}\n")


def main():

    killer = GracefulKiller()

    print("ETF Data Ingestor - Scheduled Mode")
    print("Scheduled: Every", os.getenv("INGESTOR_INTERVAL_MINUTES", "1"), "minutes")
    print("Started at:", datetime.now().strftime("%Y-%m-%d %H:%M:%S\n"))

    # Schedule the job every N minutes
    schedule.every(int(os.getenv("INGESTOR_INTERVAL_MINUTES", "1"))).minutes.do(
        ingest_job
    )

    # Run immediately on startup
    ingest_job()

    # Keep running
    while not killer.kill_now:
        schedule.run_pending()
        time.sleep(1)

    print(f"\n{'='*60}")
    print("Ingestor service stopped gracefully.")
    print("Stopped at:", datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    print(f"{'='*60}\n")

    sys.exit(0)


if __name__ == "__main__":
    main()

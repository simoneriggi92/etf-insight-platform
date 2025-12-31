import psycopg2
import json
from pathlib import Path
from datetime import datetime
from typing import List, Dict

# Database connection config
DB_CONFIG = {
    "host": "localhost",
    "port": "5432",
    "database": "etfinsight",
    "user": "etfinsight",
    "password": "devpassword123",
}


def get_db_connection():
    """Create database connection"""
    return psycopg2.connect(**DB_CONFIG)


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
        INSERT INTO etf_prices (symbol, price_date, open_price, high_price, low_price, close_price, volume)
        VALUES (%(symbol)s, %(price_date)s, %(open_price)s, %(high_price)s, %(low_price)s, %(close_price)s, %(volume)s)
        ON CONFLICT (symbol, price_date) DO NOTHING
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


def main():
    raw_dir = Path("../../data/raw")

    print(f"Loading data from {raw_dir.absolute()}\n")

    total_files = 0
    total_records = 0
    total_inserted = 0

    # Process JSON files in raw/ and raw/history

    search_patterns = [
        raw_dir.glob("*.json"),
        raw_dir.glob("history/*.json"),
    ]

    all_files = []
    for pattern in search_patterns:
        all_files.extend(pattern)

    # Process all JSON files
    for json_file in sorted(all_files):
        print(f"Processing {json_file.name}...")
        total_files += 1

        try:
            records = parse_price_file(json_file)
            total_records += len(records)

            inserted = insert_prices(records)
            total_inserted += inserted

            print(f"  Parsed {len(records)} records, inserted {inserted} new records\n")

        except Exception as e:
            print(f"  Error processing file: {e}\n")
            continue

    print(f"Summary:")
    print(f"  Files processed: {total_files}")
    print(f"  Total records parsed: {total_records}")
    print(f"  New records inserted: {total_inserted}")
    print(f"  Duplicates skipped: {total_records - total_inserted}")


if __name__ == "__main__":
    main()

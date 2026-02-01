import yfinance as yf
import json
import time
from datetime import datetime, timedelta
from pathlib import Path
from typing import Optional


def fetch_historical_data(
    symbol: str, start_date: str, end_date: str
) -> Optional[dict]:
    """
    Fetch historical price data using yfinance library

    Args:
        symbol (str): ETF symbol (e.g., 'SPY').
        start_date (str): The start date in 'YYYY-MM-DD' format.
        end_date (str): The end date in 'YYYY-MM-DD' format.

    Returns:
        dict with yfinance historical data or None if failed.
    """

    max_retries = 3
    for attempt in range(max_retries):
        try:
            print(
                f"Fetching data for {symbol} from {start_date} to {end_date} (Attempt {attempt + 1}/{max_retries}...)"
            )

            # Use yfinance to download historical data
            ticker = yf.Ticker(symbol)
            df = ticker.history(start=start_date, end=end_date, interval="1d")

            # Check if data was retrieved
            if df is None or df.empty:
                print(f"No data found for {symbol} in the given date range.")
                return None

            # Convert to dict format similar to Yahoo Finance API
            data = {"symbol": symbol, "data": df.to_dict("index")}

            return data

        except Exception as e:
            print(f"Error fetching data: {e}")
            if attempt < max_retries - 1:
                wait_time = (2**attempt) * 3  # 3s, 6s, 12s
                print(f"Waiting for {wait_time}s before retrying...")
                time.sleep(wait_time)
            else:
                print(f"Failed after {max_retries} attempts")
                return None

    return None


def save_historical_data(symbol: str, data: dict, data_range: str):
    """Save historical data to raw directory"""

    output_dir = Path("../../data/raw/history")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Convert datetime keys to strings for JSON serialization
    if "data" in data:
        data["data"] = {str(k): v for k, v in data["data"].items()}

    timestamp = datetime.now().strftime("%Y%m%d%H%M%S")
    tmp_filename = f"{symbol}_{data_range}_{timestamp}.json.tmp"
    final_filename = f"{symbol}_{data_range}_{timestamp}.json"
    tmp_filepath = output_dir / tmp_filename
    final_filepath = output_dir / final_filename

    # Write to temp file
    with open(tmp_filepath, "w") as f:
        json.dump(data, f, indent=2, default=str)

    # Atomic rename to final filename
    tmp_filepath.rename(final_filepath)

    print(f"Saved data to {final_filepath.name}")
    return final_filepath


def main():
    symbols = ["SPY", "QQQ", "VTI", "EUNL.DE", "EUNA.DE", "IS3N.DE"]

    # Fetch the last 2 yesars of data
    start_date = (datetime.now() - timedelta(days=730)).strftime("%Y-%m-%d")  # ~2 years
    end_date = datetime.now().strftime("%Y-%m-%d")

    data_range = f"{start_date}_to_{end_date}"

    print(f"Starting historical backfill")
    print(f"Date range: {start_date} to {end_date}\n")
    print(f"Symbols: {', '.join(symbols)}\n")
    print(f"Expected: ~500 trading days per symbol\n")

    successful = 0
    failed = 0

    for i, symbol in enumerate(symbols):
        print(f"[{i + 1}/{len(symbols)}] Processing {symbol}...")

        data = fetch_historical_data(symbol, start_date, end_date)

        if data:
            filepath = save_historical_data(symbol, data, data_range)

            # Quick validation: count data points
            try:
                count = len(data["data"])
                print(f"  Retrieved {count} trading days for {symbol}\n")
                successful += 1
            except Exception as e:
                print(f"  Saved but could not count trading days for {symbol}: {e}\n")
                failed += 1
        else:
            print(f"  Failed to fetch data for {symbol}\n")
            failed += 1

        # Rate limiting delay
        if i < len(symbols) - 1:
            wait_time = 5  # seconds
            print(f"Waiting for {wait_time}s before next symbol...\n")
            time.sleep(wait_time)

    print(f"Backfill complete.")
    print(f"  Successful: {successful}/{len(symbols)}")
    print(f"  Failed: {failed}/{len(symbols)}")
    print(f"\nNext step: Run load_to_db.py to insert historical data into Postgres")


if __name__ == "__main__":
    main()

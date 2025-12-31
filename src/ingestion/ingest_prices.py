import requests
import json
import time
from datetime import datetime
from pathlib import Path


def fetch_etf_price(symbol: str, max_retries: int = 3) -> dict:
    """Fetch current price for ETF symbol from Yahoo Finance"""
    url = f"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}"
    params = {"interval": "1d", "range": "5d"}  # Last 5 days

    # Custom headers to mimic a browser request
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
    }

    for attempt in range(max_retries):
        try:
            print(f"Fetching {symbol} (Attempt {attempt + 1}/{max_retries})...")
            response = requests.get(url, params=params, headers=headers, timeout=10)
            response.raise_for_status()
            return response.json()

        except requests.exceptions.HTTPError as http_err:
            if response.status_code == 429:  # Rate limit
                wait_time = (2**attempt) * 2  # Exponential backoff 2s, 4s, 8s
                print(f"Rate limited. Waiting {wait_time} seconds before retrying...")
                time.sleep(wait_time)
            else:
                print(f"HTTP error occurred: {http_err}")
                raise
        except requests.RequestException as e:
            print(f"Attempt {attempt + 1} failed: {e}")
            if attempt < max_retries - 1:
                time.sleep(2)
            else:
                raise

    raise Exception(f"Failed to fetch data for {symbol} after {max_retries} attempts")


def save_raw_response(symbol: str, data: dict):
    """Save raw API response to disk"""

    output_dir = Path("../../data/raw")
    output_dir.mkdir(parents=True, exist_ok=True)

    filename = f"{symbol}_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
    filepath = output_dir / filename

    with open(filepath, "w") as f:
        json.dump(data, f, indent=2)

    print(f"Saved data to {filepath}")


def main():
    etf_symbols = [
        "SPY",  # SPDR S&P 500 ETF Trust
        "QQQ",  # Invesco QQQ Trust
        "VTI",  # Vanguard Total Stock Market ETF
    ]

    for i, symbol in enumerate(etf_symbols):
        try:
            data = fetch_etf_price(symbol)
            save_raw_response(symbol, data)
            print(f"Successfully processed {symbol}\n")

            # Rate limiting: wait 5 seconds between requests
            if i < len(etf_symbols) - 1:  # Don't wait after the last symbol
                wait_time = 3  # seconds between requests
                print(f"Waiting {wait_time} seconds before next request...\n")
                time.sleep(wait_time)

        except Exception as e:
            print(f"Error fetching data for {symbol}: {e}")


if __name__ == "__main__":
    print(f"Starting ingestion at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    main()
    print(f"Completed at {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

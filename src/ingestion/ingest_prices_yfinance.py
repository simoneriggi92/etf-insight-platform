import yfinance as yf
import json
from datetime import datetime
from pathlib import Path


def fetch_etf_price(symbol: str) -> dict:
    """Fetch historical prices using yfinance library"""
    print(f"Fetching {symbol}...")

    ticker = yf.Ticker(symbol)

    # Get last 5 days of data
    hist = ticker.history(period="5d")

    # Convert to dict format similar to Yahoo Finance API
    data = {"symbol": symbol, "data": hist.to_dict("index")}

    return data


def save_raw_response(symbol: str, data: dict):
    """Save raw API response to disk"""
    output_dir = Path("../../data/raw")
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
    symbols = ["SPY", "QQQ", "VTI"]

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

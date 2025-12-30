"""Test script to debug yfinance issues"""

import yfinance as yf

print("Testing yfinance...")
print("\nMethod 1: Using Ticker object")
try:
    spy = yf.Ticker("SPY")
    print(f"Ticker info keys: {list(spy.info.keys())[:5]}")
    hist = spy.history(period="5d")
    print(f"History shape: {hist.shape}")
    print(f"History:\n{hist}")
except Exception as e:
    print(f"Error: {e}")

print("\n" + "=" * 50)
print("\nMethod 2: Using download")
try:
    data = yf.download("SPY", start="2025-12-20", end="2025-12-27", progress=False)
    print(f"Data shape: {data.shape}")
    print(f"Data:\n{data}")
except Exception as e:
    print(f"Error: {e}")

print("\n" + "=" * 50)
print("\nMethod 3: Testing with longer period")
try:
    data = yf.download("SPY", period="3mo", progress=False)
    print(f"Data shape: {data.shape}")
    if len(data) > 0:
        print(f"Date range: {data.index[0]} to {data.index[-1]}")
        print(f"Last 5 rows:\n{data.tail()}")
except Exception as e:
    print(f"Error: {e}")

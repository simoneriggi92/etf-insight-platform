import sys
import os

# Make include/ importable when running pytest from project root
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", ".."))

import pytest
from airflow.include.transforms.prices import normalize_prices, validate_prices


class TestNormalizePrices:
    def test_basic_normalization(self):
        raw = [
            {
                "Date": "2025-01-10",
                "Open": 100.0,
                "High": 105.0,
                "Low": 99.0,
                "Close": 103.0,
                "Volume": 1000,
            }
        ]
        result = normalize_prices(raw, "SPY")
        assert len(result) == 1
        assert result[0]["ticker"] == "SPY"
        assert result[0]["close_price"] == 103.0
        assert result[0]["price_date"] == "2025-01-10"
        assert result[0]["volume"] == 1000

    def test_all_ohlcv_fields_present(self):
        raw = [
            {
                "Date": "2025-01-10",
                "Open": 99.0,
                "High": 106.0,
                "Low": 98.0,
                "Close": 104.0,
                "Volume": 5000,
            }
        ]
        result = normalize_prices(raw, "QQQ")
        r = result[0]
        assert all(
            k in r
            for k in [
                "ticker",
                "price_date",
                "open_price",
                "high_price",
                "low_price",
                "close_price",
                "volume",
            ]
        )

    def test_skips_malformed_close(self):
        raw = [
            {
                "Date": "2025-01-10",
                "Open": 100.0,
                "High": 105.0,
                "Low": 99.0,
                "Close": "not_a_number",
                "Volume": 1000,
            }
        ]
        assert normalize_prices(raw, "SPY") == []

    def test_handles_multiple_rows(self):
        raw = [
            {
                "Date": "2025-01-10",
                "Open": 100.0,
                "High": 105.0,
                "Low": 99.0,
                "Close": 103.0,
                "Volume": 1000,
            },
            {
                "Date": "2025-01-11",
                "Open": 103.0,
                "High": 107.0,
                "Low": 102.0,
                "Close": 106.0,
                "Volume": 2000,
            },
        ]
        result = normalize_prices(raw, "SPY")
        assert len(result) == 2
        assert result[1]["price_date"] == "2025-01-11"

    def test_empty_input_returns_empty(self):
        assert normalize_prices([], "SPY") == []


class TestValidatePrices:
    def _make(self, **overrides) -> list[dict]:
        base = {
            "ticker": "SPY",
            "price_date": "2025-01-10",
            "open_price": 100.0,
            "high_price": 105.0,
            "low_price": 99.0,
            "close_price": 103.0,
            "volume": 1000,
        }
        return [{**base, **overrides}]

    def test_valid_record_passes(self):
        assert len(validate_prices(self._make())) == 1

    def test_drops_zero_close(self):
        assert validate_prices(self._make(close_price=0)) == []

    def test_drops_negative_close(self):
        assert validate_prices(self._make(close_price=-5.0)) == []

    def test_drops_inverted_high_low(self):
        assert validate_prices(self._make(high_price=90.0, low_price=105.0)) == []

    def test_drops_empty_price_date(self):
        assert validate_prices(self._make(price_date="")) == []

    def test_drops_none_price_date(self):
        assert validate_prices(self._make(price_date=None)) == []

    def test_valid_batch_filters_one_bad(self):
        records = self._make() + self._make(close_price=-1.0)
        result = validate_prices(records)
        assert len(result) == 1
        assert result[0]["close_price"] == 103.0

    def test_empty_input_returns_empty(self):
        assert validate_prices([]) == []

"""End-to-end smoke test for JIT ingestion.

This test is intentionally opt-in because it requires a live stack:

    ETF_JIT_E2E_ENABLED=1 python3 -m unittest tests.e2e.test_jit_ingestion_smoke

Environment variables:

- ETF_API_BASE_URL: base URL of the API, default http://localhost:5001
- ETF_JIT_TIMEOUT_SECONDS: poll timeout, default 180
- ETF_JIT_POLL_INTERVAL_SECONDS: poll interval, default 3
- ETF_JIT_TEST_TICKERS: comma-separated candidate tickers expected to be valid
  on yfinance but not yet present in etf_metadata
"""

from __future__ import annotations

import json
import os
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import date
from typing import Dict, Iterable, Optional, Tuple


BASE_URL = os.getenv("ETF_API_BASE_URL", "http://localhost:5001").rstrip("/")
TIMEOUT_SECONDS = int(os.getenv("ETF_JIT_TIMEOUT_SECONDS", "180"))
POLL_INTERVAL_SECONDS = float(os.getenv("ETF_JIT_POLL_INTERVAL_SECONDS", "3"))
JIT_E2E_ENABLED = os.getenv("ETF_JIT_E2E_ENABLED", "0") == "1"
TICKER_CANDIDATES = tuple(
    ticker.strip().upper()
    for ticker in os.getenv(
        "ETF_JIT_TEST_TICKERS",
        "VWCE.DE,IUSQ.DE,VUSA.MI,SWDA.MI",
    ).split(",")
    if ticker.strip()
)


class JitIngestionSmokeTest(unittest.TestCase):
    @unittest.skipUnless(
        JIT_E2E_ENABLED,
        "Set ETF_JIT_E2E_ENABLED=1 to run JIT end-to-end smoke tests.",
    )
    def test_create_portfolio_add_unknown_ticker_and_wait_until_ready(self) -> None:
        guest_id = None
        portfolio_id = None

        ticker = self._pick_unknown_ticker()

        create_status, create_headers, create_payload = self._request_json(
            "POST",
            "/api/portfolios",
            payload={
                "name": "jit-smoke-{suffix}".format(suffix=uuid.uuid4().hex[:8]),
                "baseCurrency": "EUR",
            },
            expected_statuses={201},
        )
        self.assertEqual(create_status, 201)

        guest_id = create_headers.get("x-guest-id")
        self.assertIsNotNone(guest_id, "Expected X-Guest-Id header on portfolio creation")

        portfolio = create_payload.get("portfolio", {})
        portfolio_id = portfolio.get("id")
        self.assertTrue(portfolio_id, "Expected portfolio id in create response")

        tx_status, _, tx_payload = self._request_json(
            "POST",
            "/api/portfolios/{portfolio_id}/transactions".format(
                portfolio_id=portfolio_id,
            ),
            payload={
                "ticker": ticker,
                "type": "BUY",
                "units": 1,
                "pricePerUnit": 100,
                "fees": 0,
                "transactionDate": date.today().isoformat(),
            },
            headers={"X-Guest-Id": guest_id},
            expected_statuses={202},
        )
        self.assertEqual(tx_status, 202, tx_payload)
        self.assertEqual(
            tx_payload.get("ingestion", {}).get("status"),
            "ingesting",
            tx_payload,
        )

        deadline = time.monotonic() + TIMEOUT_SECONDS
        last_status_payload = None

        while time.monotonic() < deadline:
            _, _, status_payload = self._request_json(
                "GET",
                "/api/ingestion/{ticker}/status".format(
                    ticker=urllib.parse.quote(ticker, safe=""),
                ),
                headers={"X-Guest-Id": guest_id},
                expected_statuses={200},
            )

            last_status_payload = status_payload
            current_status = str(status_payload.get("status", "")).lower()

            if current_status == "ready":
                break

            if current_status == "error":
                self.fail(
                    "JIT ingestion failed for {ticker}: {payload}".format(
                        ticker=ticker,
                        payload=status_payload,
                    )
                )

            time.sleep(POLL_INTERVAL_SECONDS)

        self.assertIsNotNone(last_status_payload, "Expected at least one status poll")
        self.assertEqual(
            str(last_status_payload.get("status", "")).lower(),
            "ready",
            "Timed out waiting for ticker {ticker}. Last payload: {payload}".format(
                ticker=ticker,
                payload=last_status_payload,
            ),
        )

        summary_status, _, summary_payload = self._request_json(
            "GET",
            "/api/portfolios/{portfolio_id}/analytics/summary".format(
                portfolio_id=portfolio_id,
            ),
            headers={"X-Guest-Id": guest_id},
            expected_statuses={200},
        )
        self.assertEqual(summary_status, 200)
        self.assertIn("twrrYtd", summary_payload)
        self.assertIsNotNone(summary_payload["twrrYtd"])

    def _pick_unknown_ticker(self) -> str:
        for ticker in TICKER_CANDIDATES:
            status, _, _ = self._request_json(
                "GET",
                "/api/ingestion/{ticker}/status".format(
                    ticker=urllib.parse.quote(ticker, safe=""),
                ),
                expected_statuses={200, 404},
            )

            if status == 404:
                return ticker

        self.skipTest(
            "No unknown candidate ticker available. Set ETF_JIT_TEST_TICKERS "
            "to valid symbols not already present in etf_metadata."
        )

    def _request_json(
        self,
        method: str,
        path: str,
        payload: Optional[Dict[str, object]] = None,
        headers: Optional[Dict[str, str]] = None,
        expected_statuses: Optional[Iterable[int]] = None,
    ) -> Tuple[int, Dict[str, str], Dict[str, object]]:
        expected = set(expected_statuses or {200})
        request_headers = {"Accept": "application/json"}

        if headers:
            request_headers.update(headers)

        body = None
        if payload is not None:
            body = json.dumps(payload).encode("utf-8")
            request_headers["Content-Type"] = "application/json"

        request = urllib.request.Request(
            url="{base}{path}".format(base=BASE_URL, path=path),
            data=body,
            headers=request_headers,
            method=method,
        )

        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                status = response.getcode()
                response_body = response.read().decode("utf-8")
                response_headers = {
                    key.lower(): value for key, value in response.headers.items()
                }
        except urllib.error.HTTPError as exc:
            status = exc.code
            response_body = exc.read().decode("utf-8")
            response_headers = {
                key.lower(): value for key, value in exc.headers.items()
            }
        except urllib.error.URLError as exc:
            self.fail(
                "Request to {url} failed: {error}".format(
                    url=request.full_url,
                    error=exc,
                )
            )

        if status not in expected:
            self.fail(
                "Unexpected status {status} for {method} {url}. Body: {body}".format(
                    status=status,
                    method=method,
                    url=request.full_url,
                    body=response_body,
                )
            )

        if not response_body:
            return status, response_headers, {}

        try:
            payload_json = json.loads(response_body)
        except json.JSONDecodeError:
            self.fail(
                "Expected JSON response from {method} {url}. Body: {body}".format(
                    method=method,
                    url=request.full_url,
                    body=response_body,
                )
            )

        self.assertIsInstance(payload_json, dict)
        return status, response_headers, payload_json

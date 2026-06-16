from __future__ import annotations

import os
import sys
import requests
from datetime import datetime, timedelta, date

from airflow import DAG
from airflow.models import Param
from airflow.operators.python import PythonOperator

sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.prices import (
    fetch_raw_prices_range,
    normalize_prices,
    validate_prices,
)

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 1,
    "retry_delay": timedelta(minutes=1),
    "retry_exponential_backoff": False,
    "email_on_failure": False,
}

CALLBACK_URL = os.environ.get(
    "DOTNET_API_CALLBACK_URL",
    "http://etf-api:8080/api/ingestion/callback",
)
CALLBACK_SECRET = os.environ.get("AIRFLOW_CALLBACK_SECRET", "")


def _fetch_and_load(**ctx) -> None:
    p = ctx["params"]
    ticker: str = p["ticker"].strip().upper()
    date_from: str = p.get("date_from", "2015-01-01")
    date_to: str = p.get("date_to", date.today().isoformat())

    if not ticker:
        raise ValueError("ticker param is required and cannot be empty.")

    hook = ETFDatabaseHook()

    # 1. Fetch raw OHLCV
    raw = fetch_raw_prices_range(ticker, start=date_from, end=date_to)
    if not raw:
        raise ValueError(
            f"yfinance returned no data for ticker '{ticker}'. Check the symbol."
        )

    # 2. Normalize + validate
    normalized = normalize_prices(raw, ticker)
    valid = validate_prices(normalized)
    print(f"[jit_ingest] {ticker}: {len(raw)} raw → {len(valid)} clean rows")

    if not valid:
        raise ValueError(
            f"All rows for '{ticker}' failed validation. Check price data quality."
        )

    # 3. Upsert prices into etf_prices
    hook.upsert_prices(valid)

    # 4. Mark ticker as ready — uses the new upsert_metadata helper from Phase 1
    hook.upsert_metadata(ticker, "ready")
    print(f"[jit_ingest] {ticker} marked as ready in etf_metadata")


def _notify_api(**ctx) -> None:
    ticker: str = ctx["params"]["ticker"].strip().upper()
    dag_run_id: str = ctx["run_id"]

    headers = {"Content-Type": "application/json"}
    if CALLBACK_SECRET:
        headers["X-Callback-Secret"] = CALLBACK_SECRET

    try:
        resp = requests.post(
            CALLBACK_URL,
            json={"ticker": ticker, "status": "ready", "dagRunId": dag_run_id},
            headers=headers,
            timeout=10,
        )
        resp.raise_for_status()
        print(f"[notify_api] Callback OK: {resp.status_code}")
    except Exception as exc:
        # Non-fatal: prices are already in the DB.
        # The .NET API can discover the status via polling.
        print(f"[notify_api] WARNING — callback failed (non-fatal): {exc}")


def _on_failure_callback(ctx) -> None:
    """Best-effort: mark ticker as 'error' so the frontend can surface the failure."""
    ticker = ctx["params"].get("ticker", "unknown").strip().upper()
    exception = ctx.get("exception")
    error_msg = str(exception) if exception else "unknown error"

    try:
        hook = ETFDatabaseHook()
        hook.upsert_metadata(ticker, "error", ingestion_error=error_msg)
        print(f"[on_failure] {ticker} marked as error: {error_msg}")
    except Exception as e:
        print(f"[on_failure] Could not update etf_metadata for {ticker}: {e}")


with DAG(
    dag_id="etf_backfill_jit",
    description="On-demand JIT price backfill for a single ETF ticker, triggered by the .NET API",
    schedule=None,  # triggered programmatically only — never runs on a schedule
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "jit", "on-demand"],
    max_active_runs=10,  # allow up to 10 concurrent single-ticker ingestions
    on_failure_callback=_on_failure_callback,
    params={
        "ticker": Param(
            "",
            type="string",
            description="ETF ticker to ingest, e.g. VUSA.MI or SWDA.MI",
        ),
        "date_from": Param(
            "2015-01-01",
            type="string",
            description="Start date (inclusive), format YYYY-MM-DD",
        ),
        "date_to": Param(
            date.today().isoformat(),
            type="string",
            description="End date (exclusive), format YYYY-MM-DD",
        ),
    },
) as dag:

    fetch_and_load = PythonOperator(
        task_id="fetch_and_load",
        python_callable=_fetch_and_load,
    )

    notify_api = PythonOperator(
        task_id="notify_api",
        python_callable=_notify_api,
    )

    fetch_and_load >> notify_api

from __future__ import annotations
import json
from datetime import datetime, timedelta
import os

from airflow import DAG
from airflow.models import Param, Variable
from airflow.operators.python import PythonOperator
from airflow.operators.trigger_dagrun import TriggerDagRunOperator
from airflow.utils.task_group import TaskGroup

import sys

sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.prices import (
    fetch_raw_prices_range,
    normalize_prices,
    validate_prices,
)

# -- defaults -------------------------------------

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 2,
    "retry_delay": timedelta(minutes=10),
    "retry_exponential_backoff": True,
    "email_on_failure": False,
}

# Symbols resolved at parse time — same source of truth as etf_daily_prices.
try:
    _SYMBOLS: list[str] = json.loads(
        Variable.get(
            "etf_static_symbols", default_var='["EUNL.DE","IS3N.DE","EUNA.DE"]'
        )
    )
except Exception:
    _SYMBOLS = ["EUNL.DE", "IS3N.DE", "EUNA.DE"]


def _validate_params(**ctx) -> None:
    """Fail fast if date params are missing or logically invalid."""
    p = ctx["params"]
    date_from = p.get("date_from", "").strip()
    date_to = p.get("date_to", "").strip()

    if not date_from or not date_to:
        raise ValueError("Both 'date_from' and 'date_to' are required.")

    try:
        d_from = datetime.strptime(date_from, "%Y-%m-%d").date()
        d_to = datetime.strptime(date_to, "%Y-%m-%d").date()
    except ValueError as e:
        raise ValueError(f"Dates must be in YYYY-MM-DD format: {e}") from e

    if d_from >= d_to:
        raise ValueError(
            f"date_from ({date_from}) must be strictly before date_to ({date_to})."
        )

    print(f"[validate_params] Params OK: {date_from} → {date_to}")


def _get_active_symbols(**ctx) -> list[str]:
    """Validates active symbols against DB and pushes list to XCom."""
    hook = ETFDatabaseHook()
    symbols = hook.get_active_symbols()
    if not symbols:
        raise ValueError("No active symbols in etf_metadata — check is_active flag.")
    ctx["ti"].xcom_push(key="active_symbols", value=symbols)
    print(f"[get_active_symbols] Found {len(symbols)} active symbols: {symbols}")
    return symbols


def _fetch_range_for_symbol(symbol: str, **ctx) -> list[dict]:
    """Fetches raw OHLCV for one symbol over the requested date range."""
    p = ctx["params"]
    date_from = p["date_from"]
    date_to = p["date_to"]
    raw = fetch_raw_prices_range(symbol, start=date_from, end=date_to)
    ctx["ti"].xcom_push(key=f"raw_{symbol}", value=raw)
    print(f"[fetch_range] {symbol}: {len(raw)} rows for {date_from} → {date_to}")
    return raw


def _normalize_and_validate(**ctx) -> list[dict]:
    """Pulls raw XCom from every fetch task, normalizes and validates."""
    ti = ctx["ti"]
    symbols = (
        ti.xcom_pull(task_ids="get_active_symbols", key="active_symbols") or _SYMBOLS
    )
    all_records: list[dict] = []

    for symbol in symbols:
        task_id = f"fetch_prices_range.fetch_{symbol.replace('.', '_')}"
        raw = ti.xcom_pull(task_ids=task_id, key=f"raw_{symbol}") or []
        normalized = normalize_prices(raw, symbol)
        valid = validate_prices(normalized)
        print(
            f"[normalize] {symbol}: {len(raw)} raw → {len(normalized)} normalized → {len(valid)} valid"
        )
        all_records.extend(valid)

    ti.xcom_push(key="clean_records", value=all_records)
    print(f"[normalize_and_validate] Total clean records: {len(all_records)}")
    return all_records


def _load_prices(**ctx) -> int:
    """Upserts clean records into etf_prices via ETFDatabaseHook."""
    ti = ctx["ti"]
    records = ti.xcom_pull(task_ids="normalize_and_validate", key="clean_records") or []
    if not records:
        print("[load_prices] No records to upsert — skipping.")
        return 0
    hook = ETFDatabaseHook()
    affected = hook.upsert_prices(records)
    print(f"[load_prices] Upserted {affected} rows from {len(records)} clean records.")
    return affected


with DAG(
    dag_id="etf_backfill_prices",
    description="Backfill ETF OHLCV for a date range via yfinance → normalize → upsert to etf_prices",
    schedule=None,  # manual trigger only
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "prices", "backfill"],
    max_active_runs=1,
    params={
        "date_from": Param(
            default=os.environ.get("BACKFILL_FROM", "2024-01-01"),
            type="string",
            description="Start date (inclusive), format YYYY-MM-DD",
        ),
        "date_to": Param(
            default=os.environ.get("BACKFILL_TO", "2026-02-28"),
            type="string",
            description="End date (exclusive), format YYYY-MM-DD",
        ),
    },
) as dag:

    validate_params = PythonOperator(
        task_id="validate_params",
        python_callable=_validate_params,
    )

    get_active_symbols = PythonOperator(
        task_id="get_active_symbols",
        python_callable=_get_active_symbols,
    )

    with TaskGroup("fetch_prices_range") as fetch_prices_group:
        for _sym in _SYMBOLS:
            PythonOperator(
                task_id=f"fetch_{_sym.replace('.', '_')}",
                python_callable=_fetch_range_for_symbol,
                op_kwargs={"symbol": _sym},
            )

    normalize_and_validate = PythonOperator(
        task_id="normalize_and_validate",
        python_callable=_normalize_and_validate,
    )

    load_prices = PythonOperator(
        task_id="load_prices",
        python_callable=_load_prices,
    )

    trigger_dq = TriggerDagRunOperator(
        task_id="trigger_dq_scan",
        trigger_dag_id="data_quality_scan",
        wait_for_completion=True,  # backfill blocks until DQ scan completes
        conf={"source_dag": "etf_backfill_prices"},
    )

    # -- dependencies --------------------------------
    (
        validate_params
        >> get_active_symbols
        >> fetch_prices_group
        >> normalize_and_validate
        >> load_prices
        >> trigger_dq
    )

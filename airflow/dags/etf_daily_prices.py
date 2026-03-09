from __future__ import annotations
import json
from datetime import datetime, timedelta

from airflow import DAG
from airflow.operators.python import PythonOperator
from airflow.models import Variable
from airflow.operators.trigger_dagrun import TriggerDagRunOperator
from airflow.utils.task_group import TaskGroup

import sys

sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.prices import (
    fetch_raw_prices,
    normalize_prices,
    validate_prices,
)

# -- defaults -------------------------------------

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 3,
    "retry_delay": timedelta(minutes=5),
    "retry_exponential_backoff": True,
    "email_on_failure": False,
}

# Symbols resolved at parse time so Airflow can build a static task graph.
# etf_static_symbols Variable must be a JSON array, e.g. '["EUNL.DE","IS3N.DE","EUNA.DE"]'
try:
    _SYMBOLS: list[str] = json.loads(
        Variable.get(
            "etf_static_symbols", default_var='["EUNL.DE","IS3N.DE","EUNA.DE"]'
        )
    )
except Exception:
    _SYMBOLS = ["EUNL.DE", "IS3N.DE", "EUNA.DE"]

# -- callables -------------------------------------


def _get_active_symbols(**ctx) -> list[str]:
    """Validates active symbols against DB and pushes list to XCom."""
    hook = ETFDatabaseHook()
    symbols = hook.get_active_symbols()
    if not symbols:
        raise ValueError("No active symbols in etf_metadata — check is_active flag.")
    ctx["ti"].xcom_push(key="active_symbols", value=symbols)
    print(f"[get_active_symbols] Found {len(symbols)} active symbols: {symbols}")
    return symbols


def _fetch_prices_for_symbol(symbol: str, **ctx) -> list[dict]:
    """Fetches raw OHLCV for one symbol via yfinance. One task per symbol."""
    period = Variable.get("etf_scraper_period", default_var="5d")
    raw = fetch_raw_prices(symbol, period)
    ctx["ti"].xcom_push(key=f"raw_{symbol}", value=raw)
    print(f"[fetch_prices] Fetched {len(raw)} rows for {symbol} period={period}")
    return raw


def _normalize_and_validate(**ctx) -> list[dict]:
    """Pulls raw XCom from every fetch task, normalizes and validates."""
    ti = ctx["ti"]
    symbols = (
        ti.xcom_pull(task_ids="get_active_symbols", key="active_symbols") or _SYMBOLS
    )
    all_records: list[dict] = []

    for symbol in symbols:
        task_id = f"fetch_prices.fetch_{symbol.replace('.', '_')}"
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


# -- DAG -------------------------------------------
with DAG(
    dag_id="etf_daily_prices",
    description="Daily ETL: fetch ETF OHLCV via yfinance → normalize → upsert to etf_prices",
    schedule="0 22 * * 1-5",  # 22:00 UTC Mon–Fri (markets closed)
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "prices", "daily"],
    max_active_runs=1,
) as dag:

    get_active_symbols = PythonOperator(
        task_id="get_active_symbols",
        python_callable=_get_active_symbols,
    )

    # One fetch task per symbol — created at parse time from etf_static_symbols Variable.
    # Airflow requires a static task graph; dynamic task creation inside callables is not supported.
    with TaskGroup("fetch_prices") as fetch_prices_group:
        for _sym in _SYMBOLS:
            PythonOperator(
                task_id=f"fetch_{_sym.replace('.', '_')}",
                python_callable=_fetch_prices_for_symbol,
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
        wait_for_completion=False,
        conf={"source_dag": "etf_daily_prices"},
    )

    # -- dependencies --------------------------------
    (
        get_active_symbols
        >> fetch_prices_group
        >> normalize_and_validate
        >> load_prices
        >> trigger_dq
    )

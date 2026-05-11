from __future__ import annotations

import sys
from datetime import datetime, timedelta

from airflow import DAG
from airflow.operators.python import PythonOperator

sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.factsheet_retrieval import retrieve_factsheet

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 1,
    "retry_delay": timedelta(minutes=10),
    "email_on_failure": False,
}

DOWNLOAD_DIR = "/opt/airflow/data/factsheets"


def _get_pending_isins(**ctx) -> list[dict]:
    hook = ETFDatabaseHook()
    isins = hook.get_isins_for_factsheet_retrieval()
    ctx["ti"].xcom_push(key="pending_isins", value=isins)
    print(f"[get_pending_isins] Found {len(isins)} ISINs needing factsheet retrieval")
    return isins


def _retrieve_factsheets(**ctx) -> None:
    ti = ctx["ti"]
    isins = ti.xcom_pull(task_ids="get_pending_isins", key="pending_isins") or []
    hook = ETFDatabaseHook()

    downloaded, failed = 0, 0
    for entry in isins:
        isin = entry["isin"]
        ticker = entry["ticker"]
        print(f"[retrieve] Processing {isin} ({ticker})...")

        result = retrieve_factsheet(isin, download_dir=DOWNLOAD_DIR)
        result["isin"] = isin
        result["ticker"] = ticker
        result["attempts"] = 1

        hook.upsert_factsheet_status(result)

        if result["status"] == "downloaded":
            downloaded += 1
            print(f"[retrieve] OK {isin} — {result['source']} -> {result['local_path']}")
        else:
            failed += 1
            print(f"[retrieve] FAIL {isin} — {result['error']}")

    print(f"[retrieve] Done: {downloaded} downloaded, {failed} failed out of {len(isins)}")


with DAG(
    dag_id="etf_knowledge_builder",
    description="Automated ETF factsheet/KIID PDF retrieval via DuckDuckGo dorking + JustETF fallback",
    schedule="0 4 * * 0",
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "knowledge", "rag", "factsheet"],
    max_active_runs=1,
) as dag:

    get_pending_isins = PythonOperator(
        task_id="get_pending_isins",
        python_callable=_get_pending_isins,
    )

    retrieve_factsheets = PythonOperator(
        task_id="retrieve_factsheets",
        python_callable=_retrieve_factsheets,
    )

    get_pending_isins >> retrieve_factsheets

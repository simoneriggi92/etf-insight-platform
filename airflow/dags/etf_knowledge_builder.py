from __future__ import annotations

import os
import sys
from datetime import datetime, timedelta

import httpx
from airflow import DAG
from airflow.operators.python import PythonOperator

sys.path.insert(0, "/opt/airflow")

from include.transforms.factsheet_chunker import process_factsheet
from include.transforms.factsheet_retrieval import inter_isin_sleep, retrieve_factsheet
from plugins.hooks.etf_db_hook import ETFDatabaseHook

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 1,
    "retry_delay": timedelta(minutes=10),
    "email_on_failure": False,
}

DOWNLOAD_DIR = "/opt/airflow/data/factsheets"
DOTNET_API_URL = os.environ.get("DOTNET_API_URL", "http://etf-api:8080")
INGEST_API_KEY = os.environ.get("INGEST_API_KEY", "")


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

        inter_isin_sleep()

    print(f"[retrieve] Done: {downloaded} downloaded, {failed} failed out of {len(isins)}")


def _parse_and_embed(**ctx) -> None:
    hook = ETFDatabaseHook()
    factsheets = hook.get_downloaded_factsheets()

    if not factsheets:
        print("[parse_and_embed] No downloaded factsheets to process")
        return

    print(f"[parse_and_embed] Processing {len(factsheets)} factsheets")

    with httpx.Client(timeout=120.0) as ollama_client:
        with httpx.Client(
                base_url=DOTNET_API_URL,
                headers={"X-API-Key": INGEST_API_KEY},
                timeout=60.0,
        ) as api_client:
            ok_count, fail_count = 0, 0
            for fs in factsheets:
                ticker = fs["ticker"]
                pdf_path = fs["local_path"]
                try:
                    chunks = process_factsheet(ticker, pdf_path, ollama_client)
                    payload = {"ticker": ticker, "chunks": chunks}
                    resp = api_client.post("/api/search/ingest", json=payload)
                    resp.raise_for_status()
                    ok_count += 1
                    print(f"[parse_and_embed] OK {ticker}: {len(chunks)} chunks ingested")
                except Exception as e:
                    fail_count += 1
                    print(f"[parse_and_embed] FAIL {ticker}: {e}")

    print(f"[parse_and_embed] Done: {ok_count} succeeded, {fail_count} failed out of {len(factsheets)}")




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

    parse_and_embed = PythonOperator(
        task_id="parse_and_embed",
        python_callable=_parse_and_embed,
    )

    get_pending_isins >> retrieve_factsheets >> parse_and_embed

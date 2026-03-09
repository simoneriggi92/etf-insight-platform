from __future__ import annotations
from datetime import datetime, timedelta

from airflow import DAG
from airflow.operators.python import PythonOperator
from airflow.providers.http.hooks.http import HttpHook
from airflow.models import Variable

# -- defaults -------------------------------------

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 2,
    "retry_delay": timedelta(minutes=2),
    "email_on_failure": False,
}


# -- callable -------------------------------------
def _run_dq_scan(**ctx) -> dict:
    """
    POST to the endpoint stored in the 'dq_webhook_path' Airflow Variable.
    Endpoint enqueues a Hangfire job and returns 202 Accepted immediately.
    Mirrors trigger_data_quality_scan() in load_to_db.py.
    """

    endpoint = Variable.get("dq_webhook_path", default_var="/api/data-quality/scan")
    source_dag = (ctx["dag_run"].conf or {}).get("source_dag", "manual")

    hook = HttpHook(method="POST", http_conn_id="etf_api")
    response = hook.run(
        endpoint=endpoint,
        headers={"Content-Type": "application/json"},
        extra_options={"timeout": 30},
    )

    payload = response.json()
    print(
        f"[data_quality_scan] Triggered by '{source_dag}' — "
        f"HTTP {response.status_code} | jobId={payload.get('jobId')}"
    )
    return payload


# -- DAG ------------------------------------------

with DAG(
    dag_id="data_quality_scan",
    description="POST dq_webhook_path — triggered by etf_daily_prices and etf_backfill_prices",
    schedule=None,
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "data-quality"],
    max_active_runs=3,
) as dag:

    PythonOperator(
        task_id="run_dq_scan",
        python_callable=_run_dq_scan,
    )

# Airflow Integration Plan — ETF Insight Platform

**Goal:** Replace the current `schedule`-based scraper/ingestor with Apache Airflow DAGs, implementing a proper ETL pipeline with dependency management, retry logic, and centralized monitoring.

---

## Table of Contents

1. [Current State](#1-current-state)
2. [Architecture Overview](#2-architecture-overview)
3. [Infrastructure Changes](#3-infrastructure-changes)
4. [Project Structure](#4-project-structure)
5. [Step-by-Step Implementation](#5-step-by-step-implementation)
   - [Step 1 — Airflow Docker Setup](#step-1--airflow-docker-setup)
   - [Step 2 — Airflow Connections & Variables](#step-2--airflow-connections--variables)
   - [Step 3 — Shared Hooks & Utilities](#step-3--shared-hooks--utilities)
   - [Step 4 — DAG: `etf_daily_prices`](#step-4--dag-etf_daily_prices)
   - [Step 5 — DAG: `etf_backfill_prices`](#step-5--dag-etf_backfill_prices)
   - [Step 6 — DAG: `data_quality_scan`](#step-6--dag-data_quality_scan)
6. [Migration Strategy](#6-migration-strategy)
7. [Testing Plan](#7-testing-plan)

---

## 1. Current State

### Surviving Ingestion Scripts

Only **three active scripts** remain after cleanup:

| File                        | Role                                                                                                          | Mode                                                                   |
| --------------------------- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `ingest_prices_yfinance.py` | Fetches OHLCV data via yfinance, writes JSON to shared volume                                                 | Scheduled (every N min) **or** backfill (via `--from`/`--to` CLI args) |
| `load_to_db.py`             | Reads JSON files from shared volume, upserts to `etf_prices` via `ON CONFLICT DO UPDATE`, triggers DQ webhook | Scheduled (every M min)                                                |
| `graceful_killer.py`        | SIGTERM/SIGINT handler — sets `kill_now` flag for both scripts                                                | Support lib                                                            |

`test_yfinance.py` is a dev-time smoke test and has no role in production.

### Pain Points Being Solved

| #   | Problem                                                                                                                |
| --- | ---------------------------------------------------------------------------------------------------------------------- |
| 1   | No orchestration — scraper and ingestor are **only time-coupled**, no dependency tracking                              |
| 2   | No backfill DAG — historical data fetches require manual CLI invocation of `ingest_prices_yfinance.py --from … --to …` |
| 3   | Failures go to `.error.log` files — no centralized visibility                                                          |
| 4   | `schedule` library has no state persistence or distributed coordination                                                |
| 5   | Shared volume is an implicit coupling — brittle and opaque                                                             |

---

## 2. Architecture Overview

### Current vs. Target

```mermaid
graph TD
    subgraph "CURRENT — schedule-based"
        S1[ingest_prices_yfinance.py\nschedule every 2 min]
        S2[load_to_db.py\nschedule every 1 min]
        VOL[(Shared Volume\n/data/raw)]
        S1 -->|writes JSON| VOL
        VOL -->|reads JSON| S2
        S2 -->|webhook POST| DQ[Hangfire DQ Scan]
    end

    subgraph "TARGET — Airflow DAGs"
        D1[etf_daily_prices\n0 22 MON-FRI]
        D2[etf_backfill_prices\nmanual + parameterized]
        D3[data_quality_scan\ntriggered]
        D1 -->|TriggerDagRunOperator| D3
        D2 -->|TriggerDagRunOperator| D3
    end
```

### Full ETL Data Flow

```mermaid
flowchart LR
    subgraph EXTRACT
        E1[get_active_symbols\nPostgresHook → etf_metadata]
        E2[fetch_prices_per_ticker\nyfinance API]
    end

    subgraph TRANSFORM
        T1[normalize_prices\ndate casting, type safety]
        T2[validate_schema\nnull checks, range guards]
    end

    subgraph LOAD
        L1[upsert_etf_prices\nON CONFLICT DO UPDATE]
        L2[trigger_dq_scan\nTriggerDagRunOperator]
    end

    E1 -->|XCom: symbol list| E2
    E2 -->|XCom: raw OHLCV| T1
    T1 --> T2
    T2 -->|XCom: clean records| L1
    L1 --> L2
```

### DAG Dependency Map

```mermaid
graph LR
    A[etf_daily_prices\n0 22 MON-FRI] -->|TriggerDagRun on success| D[data_quality_scan]
    B[etf_backfill_prices\nmanual] -->|TriggerDagRun on success| D
```

---

## 3. Infrastructure Changes

### What Gets Removed

| Remove                                                 | Reason                                              |
| ------------------------------------------------------ | --------------------------------------------------- |
| `scraper` Docker service (`ingest_prices_yfinance.py`) | Replaced by `etf_daily_prices` DAG                  |
| `ingestor` Docker service (`load_to_db.py`)            | Replaced by transform + load tasks in the same DAG  |
| `schedule` Python library                              | Replaced by Airflow scheduler                       |
| `graceful_killer.py`                                   | Airflow handles task termination — no longer needed |
| `etf_data` shared volume                               | Data passed via XCom; volume coupling eliminated    |

### What Gets Added

| Add                 | Role                                                          |
| ------------------- | ------------------------------------------------------------- |
| `airflow-webserver` | Airflow UI on port `8090`                                     |
| `airflow-scheduler` | DAG scheduling engine                                         |
| `airflow-init`      | One-time DB migration + admin user creation                   |
| `postgres-airflow`  | Isolated Postgres for Airflow metadata (separate from ETF DB) |
| `airflow/` folder   | DAGs, plugins, hooks, transforms, tests                       |

---

## 4. Project Structure

```
etf-insight-platform/
├── airflow/
│   ├── dags/
│   │   ├── etf_daily_prices.py         ← core daily ETL
│   │   ├── etf_backfill_prices.py      ← parameterized historical backfill
│   │   └── data_quality_scan.py        ← triggered DQ scan
│   ├── plugins/
│   │   └── hooks/
│   │       └── etf_db_hook.py          ← custom PostgresHook wrapper
│   ├── include/
│   │   └── transforms/
│   │       └── prices.py               ← normalize + validate OHLCV records
│   ├── tests/
│   │   ├── test_dag_integrity.py
│   │   └── test_transforms.py
│   └── requirements.txt
├── infra/
│   └── docker-compose.yml              ← extended with Airflow services
├── src/
│   └── ingestion/                      ← to be retired after cutover
│       ├── ingest_prices_yfinance.py
│       ├── load_to_db.py
│       └── graceful_killer.py
└── docs/
    └── plan.md                         ← this file
```

---

## 5. Step-by-Step Implementation

---

### Step 1 — Airflow Docker Setup

#### 1a. Extend `docker-compose.yml`

```yaml
x-airflow-common: &airflow-common
  image: apache/airflow:2.9.2
  environment:
    AIRFLOW__CORE__EXECUTOR: LocalExecutor
    AIRFLOW__DATABASE__SQL_ALCHEMY_CONN: postgresql+psycopg2://airflow:airflow@postgres-airflow:5432/airflow
    AIRFLOW__CORE__FERNET_KEY: ${AIRFLOW_FERNET_KEY}
    AIRFLOW__CORE__LOAD_EXAMPLES: "false"
    AIRFLOW__WEBSERVER__SECRET_KEY: ${AIRFLOW_SECRET_KEY}
    # ETF DB — available to all tasks via env
    ETF_DB_HOST: postgres
    ETF_DB_PORT: 5432
    ETF_DB_NAME: ${POSTGRES_DB}
    ETF_DB_USER: ${POSTGRES_USER}
    ETF_DB_PASSWORD: ${POSTGRES_PASSWORD}
    DATA_QUALITY_WEBHOOK_URL: http://etf-api:8080/api/data-quality/scan
  volumes:
    - ../airflow/dags:/opt/airflow/dags
    - ../airflow/plugins:/opt/airflow/plugins
    - ../airflow/include:/opt/airflow/include
    - airflow_logs:/opt/airflow/logs
  depends_on:
    postgres-airflow:
      condition: service_healthy
  networks:
    - etf-network

services:
  postgres-airflow:
    image: postgres:16
    environment:
      POSTGRES_USER: airflow
      POSTGRES_PASSWORD: airflow
      POSTGRES_DB: airflow
    volumes:
      - postgres_airflow_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "airflow"]
      interval: 10s
      retries: 5
    networks:
      - etf-network

  airflow-init:
    <<: *airflow-common
    entrypoint: /bin/bash
    command:
      - -c
      - |
        airflow db migrate &&
        airflow users create \
          --username admin --password admin \
          --firstname Admin --lastname User \
          --role Admin --email admin@etf-insight.local
    restart: "no"

  airflow-webserver:
    <<: *airflow-common
    command: webserver
    ports:
      - "8090:8080"
    healthcheck:
      test: ["CMD", "curl", "--fail", "http://localhost:8080/health"]
      interval: 30s
      retries: 5
    restart: unless-stopped

  airflow-scheduler:
    <<: *airflow-common
    command: scheduler
    restart: unless-stopped

volumes:
  postgres_airflow_data:
  airflow_logs:
```

#### 1b. `airflow/requirements.txt`

```txt
apache-airflow==2.9.2
apache-airflow-providers-postgres==5.10.0
apache-airflow-providers-http==4.10.0
yfinance==0.2.40
psycopg2-binary==2.9.9
requests==2.31.0
python-dotenv==1.0.1
pandas==2.2.2
```

---

### Step 2 — Airflow Connections & Variables

Run once after `airflow-init` completes, or configure via the Airflow UI.

```bash
# ETF PostgreSQL — mirrors existing DB_CONFIG in ingest_prices_yfinance.py / load_to_db.py
airflow connections add 'etf_postgres' \
  --conn-type postgres \
  --conn-host postgres \
  --conn-login ${POSTGRES_USER} \
  --conn-password ${POSTGRES_PASSWORD} \
  --conn-schema ${POSTGRES_DB} \
  --conn-port 5432

# .NET API — mirrors DATA_QUALITY_WEBHOOK_URL in load_to_db.py
airflow connections add 'etf_api' \
  --conn-type http \
  --conn-host http://etf-api \
  --conn-port 8080
```

```bash
# Mirrors PERIOD env var in ingest_prices_yfinance.py
airflow variables set etf_scraper_period "5d"

# Comma-separated list of symbols — drives dynamic task generation in DAGs
# Kept in sync with etf_metadata.is_active via the get_active_symbols task
airflow variables set etf_static_symbols "SPY,QQQ,VTI,EUNL.DE,EUNA.DE,IS3N.DE"

# Mirrors dq webhook path used in load_to_db.py trigger_data_quality_scan()
airflow variables set data_quality_webhook_url "http://etf-api:8080/api/data-quality/scan"
```

---

### Step 3 — Shared Hooks & Utilities

#### `etf_db_hook.py`

Wraps `PostgresHook` with the same upsert SQL currently in `load_to_db.py → insert_prices()`.

```python
# airflow/plugins/hooks/etf_db_hook.py
from airflow.providers.postgres.hooks.postgres import PostgresHook


class ETFDatabaseHook(PostgresHook):
    """PostgresHook with ETF-specific upsert helpers."""

    conn_name_attr = "etf_postgres_conn_id"
    default_conn_name = "etf_postgres"

    def get_active_symbols(self) -> list[str]:
        """Mirrors get_active_etf_symbols() in ingest_prices_yfinance.py."""
        rows = self.get_records(
            "SELECT ticker FROM etf_metadata WHERE is_active = TRUE ORDER BY ticker"
        )
        return [r[0] for r in rows]

    def upsert_prices(self, records: list[dict]) -> int:
        """
        Mirrors insert_prices() in load_to_db.py.
        Uses the same ON CONFLICT (symbol, price_date) DO UPDATE logic.
        """
        if not records:
            return 0
        sql = """
            INSERT INTO etf_prices
                (symbol, price_date, open_price, high_price, low_price,
                 close_price, volume)
            VALUES
                (%(symbol)s, %(price_date)s, %(open)s, %(high)s, %(low)s,
                 %(close)s, %(volume)s)
            ON CONFLICT (symbol, price_date)
            DO UPDATE SET
                open_price  = EXCLUDED.open_price,
                high_price  = EXCLUDED.high_price,
                low_price   = EXCLUDED.low_price,
                close_price = EXCLUDED.close_price,
                volume      = EXCLUDED.volume,
                created_at  = now();
        """
        conn = self.get_conn()
        cur = conn.cursor()
        cur.executemany(sql, records)
        affected = cur.rowcount
        conn.commit()
        cur.close()
        return affected

```

#### `prices.py` — Transform helpers

Extracts the data fetching and normalization logic from `ingest_prices_yfinance.py` and `load_to_db.py → parse_price_file()` into pure, testable functions.

```python
# airflow/include/transforms/prices.py
from __future__ import annotations
import pandas as pd
import yfinance as yf


def fetch_raw_prices(symbol: str, period: str = "5d") -> list[dict]:
    """
    Extract: replaces the scheduled branch of fetch_etf_price() in
    ingest_prices_yfinance.py.
    """
    df = yf.Ticker(symbol).history(period=period)
    if df.empty:
        return []
    df.index = pd.to_datetime(df.index).date
    df.reset_index(inplace=True)
    return df.to_dict(orient="records")


def fetch_raw_prices_range(symbol: str, start: str, end: str) -> list[dict]:
    """
    Extract: replaces the backfill branch of fetch_etf_price() —
    previously triggered via CLI --from / --to args.
    """
    df = yf.Ticker(symbol).history(start=start, end=end)
    if df.empty:
        return []
    df.index = pd.to_datetime(df.index).date
    df.reset_index(inplace=True)
    return df.to_dict(orient="records")


def normalize_prices(raw: list[dict], symbol: str) -> list[dict]:
    """
    Transform: replaces parse_price_file() in load_to_db.py.
    Casts types, renames columns, attaches symbol.
    """
    result = []
    for row in raw:
        price_date = row.get("Date") or row.get("price_date")
        if isinstance(price_date, pd.Timestamp):
            price_date = price_date.date()
        try:
            result.append({
                "symbol":     symbol,
                "price_date": str(price_date),
                "open":       float(row.get("Open", 0)),
                "high":       float(row.get("High", 0)),
                "low":        float(row.get("Low", 0)),
                "close":      float(row.get("Close", 0)),
                "volume":     int(row.get("Volume", 0)),
            })
        except (TypeError, ValueError):
            continue
    return result


def validate_prices(records: list[dict]) -> list[dict]:
    """
    Transform: basic sanity checks before upsert.
    Mirrors the implicit guards in insert_prices() in load_to_db.py.
    """
    return [
        r for r in records
        if r["close"] > 0
        and r["high"] >= r["low"]
        and r["price_date"]
    ]
```

---

### Step 4 — DAG: `etf_daily_prices`

Replaces the **scheduled mode** of `ingest_prices_yfinance.py` + `load_to_db.py` in a single, dependency-aware DAG.

#### Task Flow

```mermaid
flowchart LR
    A[get_active_symbols] --> B

    subgraph B[fetch_prices — TaskGroup]
        B1[fetch_SPY]
        B2[fetch_QQQ]
        B3[fetch_EUNL_DE]
        B4[...]
    end

    B --> C[normalize_and_validate]
    C --> D[load_prices]
    D --> E[trigger_dq_scan]
```

```python
# airflow/dags/etf_daily_prices.py
from __future__ import annotations
from datetime import datetime, timedelta

from airflow import DAG
from airflow.models import Variable
from airflow.operators.python import PythonOperator
from airflow.operators.trigger_dagrun import TriggerDagRunOperator
from airflow.utils.task_group import TaskGroup

import sys
sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.prices import fetch_raw_prices, normalize_prices, validate_prices

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 3,
    "retry_delay": timedelta(minutes=5),
    "retry_exponential_backoff": True,
    "email_on_failure": False,
}


def _get_active_symbols(**ctx) -> list[str]:
    symbols = ETFDatabaseHook().get_active_symbols()
    if not symbols:
        raise ValueError("No active symbols in etf_metadata.")
    ctx["ti"].xcom_push(key="symbols", value=symbols)
    return symbols


def _fetch_prices_for_symbol(symbol: str, **ctx) -> list[dict]:
    period = Variable.get("etf_scraper_period", default_var="5d")
    raw = fetch_raw_prices(symbol, period=period)
    ctx["ti"].xcom_push(key=f"raw_{symbol}", value=raw)
    return raw


def _normalize_and_validate(**ctx) -> list[dict]:
    ti = ctx["ti"]
    symbols = ti.xcom_pull(task_ids="get_active_symbols", key="symbols")
    all_records: list[dict] = []
    for symbol in symbols:
        task_id = f"fetch_prices.fetch_{symbol.replace('.', '_')}"
        raw = ti.xcom_pull(task_ids=task_id, key=f"raw_{symbol}") or []
        all_records.extend(validate_prices(normalize_prices(raw, symbol)))
    ti.xcom_push(key="clean_records", value=all_records)
    return all_records


def _load_prices(**ctx) -> int:
    records = ctx["ti"].xcom_pull(task_ids="normalize_and_validate", key="clean_records") or []
    affected = ETFDatabaseHook().upsert_prices(records)
    print(f"[load_prices] Upserted {affected} rows from {len(records)} records.")
    return affected


with DAG(
    dag_id="etf_daily_prices",
    description="Daily ETL: yfinance → normalize → upsert etf_prices → trigger DQ scan",
    schedule="0 22 * * 1-5",
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

    with TaskGroup(group_id="fetch_prices") as fetch_group:
        static_symbols = Variable.get(
            "etf_static_symbols",
            default_var="SPY,QQQ,VTI,EUNL.DE,EUNA.DE,IS3N.DE",
        ).split(",")

        for sym in static_symbols:
            PythonOperator(
                task_id=f"fetch_{sym.replace('.', '_')}",
                python_callable=_fetch_prices_for_symbol,
                op_kwargs={"symbol": sym},
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

    get_active_symbols >> fetch_group >> normalize_and_validate >> load_prices >> trigger_dq
```

---

### Step 5 — DAG: `etf_backfill_prices`

Replaces the **backfill mode** of `ingest_prices_yfinance.py` (previously run manually via `--from`/`--to` CLI args). Now triggerable from the Airflow UI with date parameters.

#### Task Flow

```mermaid
flowchart LR
    A[validate_params] --> B[get_active_symbols]
    B --> C

    subgraph C[fetch_prices_range — TaskGroup]
        C1[fetch_SPY]
        C2[fetch_QQQ]
        C3[...]
    end

    C --> D[normalize_and_validate]
    D --> E[load_prices]
    E --> F[trigger_dq_scan\nwait_for_completion=True]
```

```python
# airflow/dags/etf_backfill_prices.py
from __future__ import annotations
from datetime import datetime, timedelta

from airflow import DAG
from airflow.models import Param, Variable
from airflow.operators.python import PythonOperator
from airflow.operators.trigger_dagrun import TriggerDagRunOperator
from airflow.utils.task_group import TaskGroup

import sys
sys.path.insert(0, "/opt/airflow")

from plugins.hooks.etf_db_hook import ETFDatabaseHook
from include.transforms.prices import fetch_raw_prices_range, normalize_prices, validate_prices

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 2,
    "retry_delay": timedelta(minutes=10),
}


def _validate_params(**ctx) -> None:
    p = ctx["params"]
    if not p.get("date_from") or not p.get("date_to"):
        raise ValueError("Both 'date_from' and 'date_to' are required.")
    if p["date_from"] >= p["date_to"]:
        raise ValueError("'date_from' must precede 'date_to'.")


def _get_active_symbols(**ctx) -> list[str]:
    symbols = ETFDatabaseHook().get_active_symbols()
    ctx["ti"].xcom_push(key="symbols", value=symbols)
    return symbols


def _fetch_range(symbol: str, **ctx) -> list[dict]:
    p = ctx["params"]
    raw = fetch_raw_prices_range(symbol, start=p["date_from"], end=p["date_to"])
    ctx["ti"].xcom_push(key=f"raw_{symbol}", value=raw)
    return raw


def _normalize_and_validate(**ctx) -> list[dict]:
    ti = ctx["ti"]
    symbols = ti.xcom_pull(task_ids="get_active_symbols", key="symbols")
    all_records: list[dict] = []
    for symbol in symbols:
        task_id = f"fetch_prices_range.fetch_{symbol.replace('.', '_')}"
        raw = ti.xcom_pull(task_ids=task_id, key=f"raw_{symbol}") or []
        all_records.extend(validate_prices(normalize_prices(raw, symbol)))
    ti.xcom_push(key="clean_records", value=all_records)
    return all_records


def _load_prices(**ctx) -> int:
    records = ctx["ti"].xcom_pull(task_ids="normalize_and_validate", key="clean_records") or []
    affected = ETFDatabaseHook().upsert_prices(records)
    print(f"[load_prices] Backfill upserted {affected} rows from {len(records)} records.")
    return affected


with DAG(
    dag_id="etf_backfill_prices",
    description="Parameterized backfill: replaces manual --from/--to CLI invocation of ingest_prices_yfinance.py",
    schedule=None,
    start_date=datetime(2024, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["etf", "prices", "backfill"],
    params={
        "date_from": Param("2024-01-01", type="string", description="Start date YYYY-MM-DD"),
        "date_to":   Param("2024-12-31", type="string", description="End date YYYY-MM-DD"),
    },
    max_active_runs=1,
) as dag:

    validate_params = PythonOperator(
        task_id="validate_params",
        python_callable=_validate_params,
    )

    get_active_symbols = PythonOperator(
        task_id="get_active_symbols",
        python_callable=_get_active_symbols,
    )

    with TaskGroup(group_id="fetch_prices_range") as fetch_group:
        static_symbols = Variable.get(
            "etf_static_symbols",
            default_var="SPY,QQQ,VTI,EUNL.DE,EUNA.DE,IS3N.DE",
        ).split(",")

        for sym in static_symbols:
            PythonOperator(
                task_id=f"fetch_{sym.replace('.', '_')}",
                python_callable=_fetch_range,
                op_kwargs={"symbol": sym},
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
        wait_for_completion=True,
        conf={"source_dag": "etf_backfill_prices"},
    )

    validate_params >> get_active_symbols >> fetch_group >> normalize_and_validate >> load_prices >> trigger_dq
```

---

### Step 6 — DAG: `data_quality_scan`

Replaces the `trigger_data_quality_scan()` webhook call in `load_to_db.py`. Now a proper DAG that can be triggered by other DAGs, inspected in the UI, and retried independently.

```python
# airflow/dags/data_quality_scan.py
from __future__ import annotations
from datetime import datetime, timedelta
import time, requests

from airflow import DAG
from airflow.models import Variable
from airflow.operators.python import PythonOperator

DEFAULT_ARGS = {
    "owner": "etf-platform",
    "retries": 2,
    "retry_delay": timedelta(minutes=2),
}


def _trigger_dq_scan(**ctx) -> dict:
    webhook_url = Variable.get(
        "data_quality_webhook_url",
        default_var="http://etf-api:8080/api/data-quality/scan",
    )
    source_dag = ctx["dag_run"].conf.get("source_dag", "unknown")
    print(f"[dq_scan] Triggered by: {source_dag}")

    # Mirrors the 3-attempt retry in load_to_db.py → trigger_data_quality_scan()
    for attempt in range(1, 4):
        try:
            resp = requests.post(webhook_url, timeout=15)
            resp.raise_for_status()
            result = resp.json()
            print(f"[dq_scan] Success: {result}")
            return result
        except requests.RequestException as e:
            print(f"[dq_scan] Attempt {attempt}/3 failed: {e}")
            if attempt < 3:
                time.sleep(5)
    raise RuntimeError("Data quality scan webhook failed after 3 attempts.")


with DAG(
    dag_id="data_quality_scan",
    description="Trigger .NET API DQ scan — triggered by etf_daily_prices and etf_backfill_prices",
    schedule=None,
    start_date=datetime(2025, 1, 1),
    catchup=False,
    default_args=DEFAULT_ARGS,
    tags=["dq", "quality", "triggered"],
    max_active_runs=3,
) as dag:

    PythonOperator(
        task_id="trigger_dq_scan",
        python_callable=_trigger_dq_scan,
    )
```

---

## 6. Migration Strategy

Run both systems in parallel during transition — all DB writes use `ON CONFLICT DO UPDATE / DO NOTHING`, so both paths are fully idempotent and safe to overlap.

```mermaid
gantt
    title Migration Phases
    dateFormat  YYYY-MM-DD
    section Phase 1 — Setup
    Airflow services in docker-compose   :p1a, 2026-03-09, 2d
    Connections + Variables              :p1b, after p1a, 1d
    section Phase 2 — Core DAGs
    ETFDatabaseHook + transforms         :p2a, after p1b, 1d
    etf_daily_prices DAG                 :p2b, after p2a, 2d
    data_quality_scan DAG                :p2c, after p2b, 1d
    section Phase 3 — Backfill DAG
    etf_backfill_prices DAG              :p3a, after p2c, 2d
    Run full backfill via Airflow UI     :p3b, after p3a, 1d
    section Phase 4 — Cutover
    Disable scraper + ingestor services  :p4a, after p3b, 1d
    Remove old services from compose     :p4b, after p4a, 1d
    section Phase 5 — Tests & Cleanup
    Integration + regression tests       :p5a, after p4a, 3d
    Delete src/ingestion/                :p5b, after p5a, 1d
```

### Cutover Checklist

1. Confirm `etf_daily_prices` DAG has run successfully for **3+ consecutive trading days**.
2. Comment out `scraper` and `ingestor` services in `docker-compose.yml`.
3. Monitor for 2 more days.
4. Remove `scraper` and `ingestor` service definitions and the `etf_data` volume permanently.
5. Delete `src/ingestion/ingest_prices_yfinance.py`, `load_to_db.py`, `graceful_killer.py`.

---

## 7. Testing Plan

### Unit Tests — Transform Layer

```python
# airflow/tests/test_transforms.py
import pytest
from include.transforms.prices import normalize_prices, validate_prices


class TestNormalizePrices:
    def test_basic_normalization(self):
        raw = [{"Date": "2025-01-10", "Open": 100.0, "High": 105.0,
                "Low": 99.0, "Close": 103.0, "Volume": 1000}]
        result = normalize_prices(raw, "SPY")
        assert result[0]["symbol"] == "SPY"
        assert result[0]["close"] == 103.0
        assert result[0]["price_date"] == "2025-01-10"

    def test_skips_malformed_rows(self):
        raw = [{"Date": "2025-01-10", "Close": "not_a_number"}]
        assert normalize_prices(raw, "SPY") == []


class TestValidatePrices:
    def _record(self, **overrides):
        base = {"symbol": "SPY", "price_date": "2025-01-10",
                "open": 100, "high": 105, "low": 99, "close": 103, "volume": 1000}
        return [{**base, **overrides}]

    def test_valid_record_passes(self):
        assert len(validate_prices(self._record())) == 1

    def test_drops_negative_close(self):
        assert validate_prices(self._record(close=-1)) == []

    def test_drops_inverted_high_low(self):
        assert validate_prices(self._record(high=90, low=105)) == []

    def test_drops_missing_date(self):
        assert validate_prices(self._record(price_date="")) == []
```

### DAG Integrity Tests

```python
# airflow/tests/test_dag_integrity.py
from airflow.models import DagBag

DAG_FOLDER = "airflow/dags"

def test_no_import_errors():
    bag = DagBag(dag_folder=DAG_FOLDER, include_examples=False)
    assert bag.import_errors == {}, bag.import_errors

def test_expected_dags_present():
    bag = DagBag(dag_folder=DAG_FOLDER, include_examples=False)
    expected = {"etf_daily_prices", "etf_backfill_prices", "data_quality_scan"}
    assert expected.issubset(set(bag.dag_ids))

def test_etf_daily_prices_has_trigger():
    bag = DagBag(dag_folder=DAG_FOLDER, include_examples=False)
    dag = bag.dags["etf_daily_prices"]
    task_ids = {t.task_id for t in dag.tasks}
    assert "trigger_dq_scan" in task_ids
    assert "load_prices" in task_ids

def test_backfill_dag_has_params():
    bag = DagBag(dag_folder=DAG_FOLDER, include_examples=False)
    dag = bag.dags["etf_backfill_prices"]
    assert "date_from" in dag.params
    assert "date_to" in dag.params
```

---

## TODO List

> Track implementation progress phase by phase. Each task should be completed and verified before moving to the next phase.

---

### Phase 1 — Infrastructure Setup

**Goal:** Get Airflow running locally inside the existing Docker stack.

- [x] **1.1** Generate a Fernet key for Airflow encryption:

  ```bash
  python -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())"
  ```

  Store result as `AIRFLOW_FERNET_KEY` in `infra/.env`.

- [x] **1.2** Generate a webserver secret key and store as `AIRFLOW_SECRET_KEY` in `infra/.env`.

- [x] **1.3** Add the `x-airflow-common` YAML anchor to `infra/docker-compose.yml` with all environment variables (executor, DB conn, ETF DB vars, webhook URL).

- [x] **1.4** Add `postgres-airflow` service to `docker-compose.yml` (Postgres 16, dedicated volume, healthcheck).

- [x] **1.5** Add `airflow-init` service to `docker-compose.yml` (runs `airflow db migrate` + creates admin user, `restart: "no"`).

- [x] **1.6** Add `airflow-webserver` service (port `8090:8080`, healthcheck via `/health`).

- [x] **1.7** Add `airflow-scheduler` service.

- [x] **1.8** Add `postgres_airflow_data` and `airflow_logs` to the `volumes:` block in `docker-compose.yml`.

- [x] **1.9** Mount `../airflow/dags`, `../airflow/plugins`, `../airflow/include` as volumes on all Airflow services.

- [x] **1.10** Create the `airflow/` root folder with the following empty structure:

  ```
  airflow/
  ├── dags/
  ├── plugins/hooks/
  ├── include/transforms/
  └── tests/
  ```

- [x] **1.11** Create `airflow/requirements.txt` with pinned versions (airflow 2.9.2, providers-postgres, providers-http, yfinance, psycopg2-binary, requests, pandas).

- [x] **1.12** Run `docker compose up airflow-init` and confirm it exits cleanly (code 0).

- [x] **1.13** Run `docker compose up airflow-webserver airflow-scheduler` and verify the UI is reachable at `http://localhost:8090`.

- [x] **1.14** Confirm the Airflow metadata DB (`postgres-airflow`) is isolated from the ETF DB (`postgres`) — query both to verify schema separation.

---

### Phase 2 — Connections & Variables

**Goal:** Wire Airflow to the ETF database and API using its secrets management.

- [x] **2.1** Open the Airflow UI at `http://localhost:8090` → Admin → Connections.

- [x] **2.2** Create connection `etf_postgres`:
  - Type: `Postgres`
  - Host: `postgres`, Port: `5432`
  - Schema: value of `POSTGRES_DB`
  - Login / Password: values of `POSTGRES_USER` / `POSTGRES_PASSWORD`

- [x] **2.3** Create connection `etf_api`:
  - Type: `HTTP`
  - Host: `http://etf-api`, Port: `8080`

- [x] **2.4** Open Admin → Variables and set:
  - `etf_scraper_period` → `5d`
  - `etf_static_symbols` → `SPY,QQQ,VTI,EUNL.DE,EUNA.DE,IS3N.DE` _(adjust to match `etf_metadata.is_active`)_
  - `data_quality_webhook_url` → `http://etf-api:8080/api/data-quality/scan`

- [x] **2.5** Alternatively, script all of the above via CLI (document the commands in a `airflow/setup_connections.sh` file for repeatability).

- [x] **2.6** Verify connection `etf_postgres` by running a test query from the Airflow UI (Admin → Connections → Test).

---

### Phase 3 — Shared Hooks & Transform Layer

**Goal:** Implement all shared code — hooks and pure transform functions — before any DAG.

#### Hook

- [ ] **3.1** Create `airflow/plugins/hooks/etf_db_hook.py` extending `PostgresHook`.
- [ ] **3.2** Implement `get_active_symbols()` — mirrors `get_active_etf_symbols()` in `ingest_prices_yfinance.py`.
- [ ] **3.3** Implement `upsert_prices(records)` — mirrors `insert_prices()` in `load_to_db.py` using `ON CONFLICT (symbol, price_date) DO UPDATE`.
- [ ] **3.4** Manually smoke-test the hook from a Python REPL inside the Airflow container:
  ```bash
  docker exec -it <airflow-scheduler> python -c "
  from plugins.hooks.etf_db_hook import ETFDatabaseHook
  print(ETFDatabaseHook().get_active_symbols())
  "
  ```

#### Transform — Prices

- [ ] **3.5** Create `airflow/include/transforms/prices.py`.
- [ ] **3.6** Implement `fetch_raw_prices(symbol, period)` — wraps `yf.Ticker.history()` for scheduled mode.
- [ ] **3.7** Implement `fetch_raw_prices_range(symbol, start, end)` — wraps `yf.Ticker.history()` for backfill mode.
- [ ] **3.8** Implement `normalize_prices(raw, symbol)` — replaces `parse_price_file()` in `load_to_db.py`: cast types, rename columns, attach symbol.
- [ ] **3.9** Implement `validate_prices(records)` — drop records where `close <= 0`, `high < low`, or `price_date` is empty.
- [ ] **3.10** Create `airflow/tests/test_transforms.py` with `TestNormalizePrices` and `TestValidatePrices` test classes.
- [ ] **3.11** Run tests locally: `pytest airflow/tests/test_transforms.py -v` — all must pass.

---

### Phase 4 — DAG: `etf_daily_prices`

**Goal:** Implement and validate the core daily ETL DAG that replaces the `scraper` + `ingestor` Docker services.

- [ ] **4.1** Create `airflow/dags/etf_daily_prices.py`.
- [ ] **4.2** Define `DEFAULT_ARGS` with `retries=3`, `retry_delay=5min`, `retry_exponential_backoff=True`.
- [ ] **4.3** Implement `_get_active_symbols()` task — queries `etf_metadata` via `ETFDatabaseHook`, pushes list to XCom.
- [ ] **4.4** Implement `_fetch_prices_for_symbol(symbol)` task — reads `etf_scraper_period` Variable, calls `fetch_raw_prices()`, pushes raw data to XCom.
- [ ] **4.5** Create a `TaskGroup("fetch_prices")` with one `PythonOperator` per symbol, generated from `etf_static_symbols` Variable.
- [ ] **4.6** Implement `_normalize_and_validate()` task — pulls raw XCom from each fetch task, calls `normalize_prices()` + `validate_prices()`, pushes clean records to XCom.
- [ ] **4.7** Implement `_load_prices()` task — pulls clean records from XCom, calls `ETFDatabaseHook().upsert_prices()`, logs affected row count.
- [ ] **4.8** Add `TriggerDagRunOperator` as final task pointing to `data_quality_scan` DAG (`wait_for_completion=False`).
- [ ] **4.9** Wire the dependency chain: `get_active_symbols >> fetch_group >> normalize_and_validate >> load_prices >> trigger_dq_scan`.
- [ ] **4.10** Set `schedule="0 22 * * 1-5"`, `catchup=False`, `max_active_runs=1`.
- [ ] **4.11** Verify the DAG appears in the Airflow UI with no import errors.
- [ ] **4.12** Trigger the DAG manually from the UI and confirm all tasks go green.
- [ ] **4.13** Query `etf_prices` to verify rows were inserted/updated with correct `symbol`, `price_date`, and OHLCV values.

---

### Phase 5 — DAG: `etf_backfill_prices`

**Goal:** Replace the manual `--from`/`--to` CLI invocation of `ingest_prices_yfinance.py` with a UI-triggerable parameterized DAG.

- [ ] **5.1** Create `airflow/dags/etf_backfill_prices.py`.
- [ ] **5.2** Declare `Param("date_from")` and `Param("date_to")` on the DAG with string type and example defaults.
- [ ] **5.3** Implement `_validate_params()` task — raise `ValueError` if either param is missing or `date_from >= date_to`.
- [ ] **5.4** Implement `_get_active_symbols()` task (same as daily DAG — push to XCom).
- [ ] **5.5** Implement `_fetch_range(symbol)` task — reads `date_from`/`date_to` from `ctx["params"]`, calls `fetch_raw_prices_range()`, pushes to XCom.
- [ ] **5.6** Create a `TaskGroup("fetch_prices_range")` with one task per symbol (same symbol list as daily DAG).
- [ ] **5.7** Implement `_normalize_and_validate()` and `_load_prices()` tasks (reuse same logic as daily DAG).
- [ ] **5.8** Add `TriggerDagRunOperator` pointing to `data_quality_scan` with `wait_for_completion=True`.
- [ ] **5.9** Wire chain: `validate_params >> get_active_symbols >> fetch_group >> normalize_and_validate >> load_prices >> trigger_dq_scan`.
- [ ] **5.10** Set `schedule=None` (manual trigger only), `max_active_runs=1`.
- [ ] **5.11** Verify DAG appears in the UI with no import errors.
- [ ] **5.12** Trigger the DAG from the UI with `date_from="2024-01-01"` and `date_to="2024-12-31"`, confirm all tasks green.
- [ ] **5.13** Verify `etf_prices` contains the expected date range for each symbol.
- [ ] **5.14** Test the validation task: trigger with invalid params (`date_from > date_to`) and confirm `validate_params` fails fast with a clear error.

---

### Phase 6 — DAG: `data_quality_scan`

**Goal:** Replace the `trigger_data_quality_scan()` webhook call in `load_to_db.py` with a stand-alone triggerable DAG.

- [ ] **6.1** Create `airflow/dags/data_quality_scan.py`.
- [ ] **6.2** Implement `_trigger_dq_scan()` task — reads `data_quality_webhook_url` Variable, reads `source_dag` from `dag_run.conf`, implements 3-attempt retry loop with 5s delay (mirrors existing logic in `load_to_db.py`).
- [ ] **6.3** Set `schedule=None`, `max_active_runs=3` (allow concurrent scans from daily + backfill DAGs).
- [ ] **6.4** Verify the DAG appears in the UI with no import errors.
- [ ] **6.5** Trigger the DAG manually with `{"source_dag": "manual_test"}` and confirm the `.NET` API receives the POST and responds with a valid DQ stats payload.
- [ ] **6.6** Confirm that when `etf_daily_prices` completes, `data_quality_scan` is automatically triggered and appears in the DAG run list.

---

### Phase 7 — DAG Integrity Tests

**Goal:** Ensure all DAGs parse cleanly and have the expected structure, caught by CI before deployment.

- [ ] **7.1** Create `airflow/tests/test_dag_integrity.py`.
- [ ] **7.2** Implement `test_no_import_errors()` — asserts `DagBag.import_errors == {}`.
- [ ] **7.3** Implement `test_expected_dags_present()` — asserts `etf_daily_prices`, `etf_backfill_prices`, `data_quality_scan` are all loaded.
- [ ] **7.4** Implement `test_etf_daily_prices_has_trigger()` — asserts `trigger_dq_scan` and `load_prices` task IDs exist.
- [ ] **7.5** Implement `test_backfill_dag_has_params()` — asserts `date_from` and `date_to` in `dag.params`.
- [ ] **7.6** Run `pytest airflow/tests/ -v` — all tests must pass.

---

### Phase 8 — Parallel Running (Validation Period)

**Goal:** Run both the old Docker services and the new Airflow DAGs simultaneously to validate correctness before cutover. Safe because all DB writes are idempotent (`ON CONFLICT DO UPDATE`).

- [ ] **8.1** Confirm both `scraper` and `ingestor` Docker services are still running.
- [ ] **8.2** Enable `etf_daily_prices` DAG in the Airflow UI (toggle ON).
- [ ] **8.3** At the end of day 1: compare `etf_prices` row counts and spot-check OHLCV values against the raw JSON files written by the old scraper.
- [ ] **8.4** Monitor Airflow task logs for any errors or unexpected retries for 3 consecutive trading days.
- [ ] **8.5** Verify `data_quality_scan` DAG is triggered automatically after each successful `etf_daily_prices` run.
- [ ] **8.6** Confirm DQ anomaly counts in `data_anomalies` are consistent with what the old webhook-triggered Hangfire scan was producing.
- [ ] **8.7** Document any discrepancies found and fix before cutover.

---

### Phase 9 — Cutover

**Goal:** Decommission the old ingestion services and make Airflow the sole ingestor.

- [ ] **9.1** Comment out (do not yet delete) the `scraper` and `ingestor` service definitions in `docker-compose.yml`.
- [ ] **9.2** Comment out the `etf_data` shared volume.
- [ ] **9.3** Run `docker compose up -d` to apply changes — confirm scraper and ingestor containers are stopped.
- [ ] **9.4** Monitor `etf_daily_prices` DAG for 2 more trading days with no old services running.
- [ ] **9.5** Confirm `etf_prices` continues to receive fresh data exclusively from Airflow DAG runs.
- [ ] **9.6** Once stable, permanently remove the `scraper` service, `ingestor` service, and `etf_data` volume from `docker-compose.yml`.
- [ ] **9.7** Delete `src/ingestion/ingest_prices_yfinance.py`.
- [ ] **9.8** Delete `src/ingestion/load_to_db.py`.
- [ ] **9.9** Delete `src/ingestion/graceful_killer.py`.
- [ ] **9.10** Delete `src/ingestion/Dockerfile` (both scraper and ingestor targets are gone).
- [ ] **9.11** Update `src/ingestion/requirements.txt` or remove it entirely if the folder is now empty.
- [ ] **9.12** Run a final `docker compose up -d` to confirm the stack starts cleanly with no references to removed services.

---

### Phase 10 — Backfill Historical Data

**Goal:** Use the new `etf_backfill_prices` DAG to populate any historical gaps left by the old scripts.

- [ ] **10.1** Query `etf_prices` to identify the earliest and latest dates per symbol:
  ```sql
  SELECT symbol, MIN(price_date), MAX(price_date), COUNT(*)
  FROM etf_prices
  GROUP BY symbol
  ORDER BY symbol;
  ```
- [ ] **10.2** For each symbol with gaps, trigger `etf_backfill_prices` from the UI with the appropriate `date_from` / `date_to` range.
- [ ] **10.3** After each backfill run, re-run the query above to confirm the gap is filled.
- [ ] **10.4** Trigger `data_quality_scan` manually after all backfills are complete and review `data_anomalies` for any newly detected issues.

---

_ETF Insight Platform — Airflow Migration Plan — March 2026_

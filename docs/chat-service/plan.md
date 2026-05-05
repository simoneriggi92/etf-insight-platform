# Plan: ETF Factsheet PDF Retrieval Pipeline (Phase 1)

## Objective

Build a zero-cost, automated Python pipeline inside Apache Airflow that retrieves ETF Factsheet/KIID PDF documents given only an ISIN. This is Phase 1 of a larger RAG pipeline — it covers **retrieval only** (find and download the PDF). Chunking, embedding, and vector storage are out of scope.

The pipeline uses a two-level fallback strategy:
1. **DuckDuckGo OSINT dorking** — fast, provider-agnostic, zero-cost.
2. **JustETF targeted scraping** — structured fallback when dorking fails.

## Approach

Implement a new Airflow DAG (`etf_knowledge_builder`) that:
1. Reads active ISINs from `etf_metadata` via the existing `ETFDatabaseHook`.
2. For each ISIN, attempts PDF retrieval through Level 1, then Level 2.
3. Persists downloaded PDFs to a configurable local directory (`data/raw/factsheets/`).
4. Records retrieval status in a new `etf_factsheet_status` DB table so subsequent runs skip already-acquired documents.

**Why this approach over alternatives:**
- DuckDuckGo first because it is provider-agnostic, requires no per-provider scraper maintenance, and often yields direct CDN links.
- JustETF as fallback because it aggregates European ETFs, has a predictable URL structure (`/etf-profile.html?isin={ISIN}`), and surfaces factsheet download links in a parseable HTML structure.
- No Selenium/Playwright — keeps the dependency footprint minimal and avoids headless-browser overhead in the Airflow worker. If JS-rendered pages block both levels, the ISIN is marked `failed` and logged for manual review.

## Out of Scope

- PDF text extraction, chunking, and embedding (Phase 2).
- Updating `etf_documents` / pgvector (Phase 2).
- Provider-specific scrapers beyond JustETF.
- Authentication-gated PDFs.
- Frontend UI for factsheet management.

## Files to Create

| File | Responsibility |
|---|---|
| `airflow/dags/etf_knowledge_builder.py` | DAG definition: orchestrates retrieval for all active ISINs |
| `airflow/include/transforms/factsheet_retrieval.py` | Pure retrieval logic: Level 1 (DuckDuckGo) + Level 2 (JustETF) |
| `airflow/tests/test_factsheet_retrieval.py` | Unit tests for retrieval logic (mocked HTTP) |
| `src/db/10_etf_factsheet_status_schema.sql` | Schema for tracking factsheet retrieval status per ISIN |

## Files to Modify

| File | Change |
|---|---|
| `airflow/requirements.txt` | Add `duckduckgo-search` and `beautifulsoup4` |
| `airflow/plugins/hooks/etf_db_hook.py` | Add helpers: `get_isins_for_factsheet_retrieval`, `upsert_factsheet_status` |
| `airflow/tests/test_dag_integrity.py` | Add `etf_knowledge_builder` to `EXPECTED_DAGS` and basic structure tests |

## Implementation

### 1. Database Schema

New table `etf_factsheet_status` to track retrieval state per ISIN, avoiding redundant downloads on re-runs.

```sql
-- src/db/10_etf_factsheet_status_schema.sql

CREATE TABLE IF NOT EXISTS etf_factsheet_status (
    isin        VARCHAR(12) NOT NULL PRIMARY KEY,
    ticker      VARCHAR(20) NOT NULL REFERENCES etf_metadata(ticker) ON DELETE CASCADE,
    status      VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | downloaded | failed
    source      VARCHAR(30),                              -- duckduckgo | justetf | null
    pdf_url     TEXT,
    local_path  TEXT,
    error       TEXT,
    attempts    INT NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_factsheet_status_status ON etf_factsheet_status (status);
```

### 2. Dependencies

```txt
# additions to airflow/requirements.txt
duckduckgo-search==7.5.1
beautifulsoup4==4.12.3
```

### 3. DB Hook Extensions

Add two methods to `ETFDatabaseHook` in `airflow/plugins/hooks/etf_db_hook.py`:

```python
def get_isins_for_factsheet_retrieval(self) -> list[dict]:
    """Returns ISINs that need factsheet retrieval (pending or failed with < 3 attempts)."""
    rows = self.get_records("""
        SELECT m.isin, m.ticker, m.name
        FROM etf_metadata m
        LEFT JOIN etf_factsheet_status fs ON fs.isin = m.isin
        WHERE m.is_active = TRUE
          AND m.isin IS NOT NULL
          AND (fs.isin IS NULL OR (fs.status = 'failed' AND fs.attempts < 3))
        ORDER BY m.ticker
    """)
    return [{"isin": r[0], "ticker": r[1], "name": r[2]} for r in rows]

def upsert_factsheet_status(self, record: dict) -> None:
    """Upserts a row in etf_factsheet_status."""
    sql = """
        INSERT INTO etf_factsheet_status
            (isin, ticker, status, source, pdf_url, local_path, error, attempts, updated_at)
        VALUES
            (%(isin)s, %(ticker)s, %(status)s, %(source)s, %(pdf_url)s,
             %(local_path)s, %(error)s, %(attempts)s, NOW())
        ON CONFLICT (isin) DO UPDATE SET
            status     = EXCLUDED.status,
            source     = EXCLUDED.source,
            pdf_url    = EXCLUDED.pdf_url,
            local_path = EXCLUDED.local_path,
            error      = EXCLUDED.error,
            attempts   = etf_factsheet_status.attempts + 1,
            updated_at = NOW()
    """
    conn = self.get_conn()
    cur = conn.cursor()
    cur.execute(sql, record)
    conn.commit()
    cur.close()
```

### 4. Retrieval Logic

`airflow/include/transforms/factsheet_retrieval.py` — pure functions, no Airflow imports.

```python
from __future__ import annotations

import os
import re
import requests
from duckduckgo_search import DDGS
from bs4 import BeautifulSoup

JUSTETF_BASE = "https://www.justetf.com/en/etf-profile.html"
PDF_CONTENT_TYPE = "application/pdf"
DOWNLOAD_DIR_DEFAULT = "/opt/airflow/data/factsheets"
REQUEST_TIMEOUT = 30
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
}


def retrieve_factsheet(isin: str, download_dir: str = DOWNLOAD_DIR_DEFAULT) -> dict:
    """
    Two-level fallback retrieval. Returns a dict with keys:
      status: "downloaded" | "failed"
      source: "duckduckgo" | "justetf" | None
      pdf_url: str | None
      local_path: str | None
      error: str | None
    """
    os.makedirs(download_dir, exist_ok=True)

    # Level 1: DuckDuckGo OSINT dorking
    url = _search_duckduckgo(isin)
    if url:
        path = _download_pdf(url, isin, download_dir)
        if path:
            return _success(source="duckduckgo", pdf_url=url, local_path=path)

    # Level 2: JustETF scraping
    url = _scrape_justetf(isin)
    if url:
        path = _download_pdf(url, isin, download_dir)
        if path:
            return _success(source="justetf", pdf_url=url, local_path=path)

    return _failure("Both retrieval levels failed to yield a valid PDF")


def _search_duckduckgo(isin: str) -> str | None:
    """Level 1: search DuckDuckGo for a direct factsheet PDF link."""
    query = f'{isin} "factsheet" filetype:pdf'
    try:
        with DDGS() as ddgs:
            results = list(ddgs.text(query, max_results=5))
        for r in results:
            href = r.get("href", "")
            if href.lower().endswith(".pdf"):
                return href
    except Exception:
        pass
    return None


def _scrape_justetf(isin: str) -> str | None:
    """Level 2: scrape the JustETF profile page for the factsheet download link."""
    try:
        resp = requests.get(
            JUSTETF_BASE,
            params={"isin": isin},
            headers=HEADERS,
            timeout=REQUEST_TIMEOUT,
        )
        resp.raise_for_status()
        soup = BeautifulSoup(resp.text, "html.parser")

        for link in soup.find_all("a", href=True):
            href = link["href"]
            text = link.get_text(strip=True).lower()
            if ("factsheet" in text or "kiid" in text) and href.lower().endswith(".pdf"):
                if not href.startswith("http"):
                    href = "https://www.justetf.com" + href
                return href
    except Exception:
        pass
    return None


def _download_pdf(url: str, isin: str, download_dir: str) -> str | None:
    """Downloads the PDF and validates it starts with %PDF. Returns local path or None."""
    try:
        resp = requests.get(url, headers=HEADERS, timeout=REQUEST_TIMEOUT, stream=True)
        resp.raise_for_status()

        safe_name = re.sub(r"[^A-Za-z0-9_-]", "_", isin)
        local_path = os.path.join(download_dir, f"{safe_name}_factsheet.pdf")

        with open(local_path, "wb") as f:
            for chunk in resp.iter_content(chunk_size=8192):
                f.write(chunk)

        # Validate the file is actually a PDF
        with open(local_path, "rb") as f:
            header = f.read(5)
        if header != b"%PDF-":
            os.remove(local_path)
            return None

        return local_path
    except Exception:
        return None


def _success(source: str, pdf_url: str, local_path: str) -> dict:
    return {"status": "downloaded", "source": source, "pdf_url": pdf_url,
            "local_path": local_path, "error": None}


def _failure(error: str) -> dict:
    return {"status": "failed", "source": None, "pdf_url": None,
            "local_path": None, "error": error}
```

### 5. DAG Definition

`airflow/dags/etf_knowledge_builder.py` — follows existing DAG patterns (see `etf_daily_prices.py`).

```python
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
            print(f"[retrieve] ✓ {isin} — {result['source']} → {result['local_path']}")
        else:
            failed += 1
            print(f"[retrieve] ✗ {isin} — {result['error']}")

    print(f"[retrieve] Done: {downloaded} downloaded, {failed} failed out of {len(isins)}")


with DAG(
    dag_id="etf_knowledge_builder",
    description="Automated ETF factsheet/KIID PDF retrieval via DuckDuckGo dorking + JustETF fallback",
    schedule="0 4 * * 0",  # Weekly on Sunday at 04:00 UTC
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
```

### 6. Tests

`airflow/tests/test_factsheet_retrieval.py` — unit tests with mocked HTTP.

Key test cases:
- `"returns downloaded when duckduckgo yields a valid pdf link"` — mock DDGS to return a `.pdf` URL, mock `requests.get` to return `%PDF-` content, assert `status == "downloaded"` and `source == "duckduckgo"`.
- `"falls back to justetf when duckduckgo returns no results"` — mock DDGS to return empty, mock JustETF HTML with a factsheet link, assert `source == "justetf"`.
- `"returns failed when both levels yield nothing"` — mock both to return empty, assert `status == "failed"`.
- `"rejects non-pdf content even if url ends in .pdf"` — mock download to return HTML, assert `local_path is None`.

Update `test_dag_integrity.py`:
- Add `"etf_knowledge_builder"` to `EXPECTED_DAGS`.
- Add test: `test_knowledge_builder_dag_task_ids` asserts `get_pending_isins` and `retrieve_factsheets` are present.
- Add test: `test_knowledge_builder_schedule` asserts `schedule_interval == "0 4 * * 0"` and `max_active_runs == 1`.

## Schema / Type Changes

### New Table: `etf_factsheet_status`

| Column | Type | Notes |
|---|---|---|
| `isin` | `VARCHAR(12) PK` | References `etf_metadata.isin` logically (no FK — isin is not unique in `etf_metadata` today, see VTI/VGT duplicate) |
| `ticker` | `VARCHAR(20) NOT NULL` | FK to `etf_metadata.ticker` |
| `status` | `VARCHAR(20)` | `pending`, `downloaded`, `failed` |
| `source` | `VARCHAR(30)` | `duckduckgo`, `justetf`, or null |
| `pdf_url` | `TEXT` | Original URL the PDF was downloaded from |
| `local_path` | `TEXT` | Filesystem path to the downloaded PDF |
| `error` | `TEXT` | Error message on failure |
| `attempts` | `INT` | Incremented on each retry; capped at 3 in the query |
| `created_at` | `TIMESTAMPTZ` | Row creation time |
| `updated_at` | `TIMESTAMPTZ` | Last update time |

No changes to existing tables.

## Migration Strategy

1. Run `src/db/10_etf_factsheet_status_schema.sql` against the database manually or via the existing numbered-script convention.
2. No EF Core migration needed — this table is consumed exclusively by the Airflow Python layer.

## Considerations & Trade-offs

1. **Sequential processing per ISIN** — the `retrieve_factsheets` task processes ISINs in a loop rather than spawning one task per ISIN. This avoids a dynamic task graph (not supported cleanly in Airflow 2.x with static-only DAGs in this project's pattern) and keeps DuckDuckGo/JustETF request rate low to avoid rate-limiting. Trade-off: slower total wall time for large ISIN sets.

2. **No FK on `isin`** — `etf_metadata.isin` is not unique (VTI and VGT share `US9229087690`). The `etf_factsheet_status` table uses `isin` as PK, which means one factsheet per ISIN regardless of how many tickers share it. This is correct for factsheets (one document per ISIN), but we use `ticker` as a display/reference column with an FK for cascade deletes.

3. **No headless browser** — JS-heavy provider pages will fail both levels. This is acceptable for Phase 1 — the `failed` status with `attempts < 3` retry logic allows manual intervention or a future Level 3 (Playwright) without schema changes.

4. **Rate limiting** — DuckDuckGo and JustETF may rate-limit aggressive scraping. The weekly schedule and sequential processing mitigate this. If rate-limiting becomes an issue, add `time.sleep()` between ISINs in the retrieval loop.

5. **PDF validation is minimal** — checking `%PDF-` header catches HTML error pages served as 200 OK, but does not validate the PDF is a factsheet vs. some other document. Phase 2 (text extraction) will surface content-level mismatches.

## Todo List

- [x] Phase 1: Database
  - [x] Task 1.1: Create `src/db/10_etf_factsheet_status_schema.sql`
  - [x] Task 1.2: Apply schema to development database
- [x] Phase 2: Dependencies
  - [x] Task 2.1: Add `duckduckgo-search` and `beautifulsoup4` to `airflow/requirements.txt`
- [ ] Phase 3: Retrieval Logic
  - [ ] Task 3.1: Create `airflow/include/transforms/factsheet_retrieval.py` with `retrieve_factsheet`, `_search_duckduckgo`, `_scrape_justetf`, `_download_pdf`
  - [ ] Task 3.2: Add `get_isins_for_factsheet_retrieval` and `upsert_factsheet_status` to `ETFDatabaseHook`
- [ ] Phase 4: DAG
  - [ ] Task 4.1: Create `airflow/dags/etf_knowledge_builder.py`
- [ ] Phase 5: Tests
  - [ ] Task 5.1: Create `airflow/tests/test_factsheet_retrieval.py` with mocked HTTP tests
  - [ ] Task 5.2: Update `airflow/tests/test_dag_integrity.py` with `etf_knowledge_builder` expectations
- [ ] Phase 6: Validation
  - [ ] Task 6.1: Run DAG integrity tests (`pytest airflow/tests/`)
  - [ ] Task 6.2: Trigger DAG manually in Airflow UI and verify at least one PDF is retrieved

---

**Do not implement yet.**


from __future__ import annotations

import os
import random
import re
import time

import requests
from bs4 import BeautifulSoup
from duckduckgo_search import DDGS

JUSTETF_BASE = "https://www.justetf.com/en/etf-profile.html"
DOWNLOAD_DIR_DEFAULT = "/opt/airflow/data/factsheets"
REQUEST_TIMEOUT = 30
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
}

DDG_SLEEP_MIN = 3.0
DDG_SLEEP_MAX = 7.0
INTER_ISIN_SLEEP_MIN = 4.0
INTER_ISIN_SLEEP_MAX = 10.0
DDG_ENABLED = os.environ.get("FACTSHEET_ENABLE_DDG", "true").lower() == "true"
DDG_RATE_LIMIT_COOLDOWN_SECONDS = float(
    os.environ.get("FACTSHEET_DDG_RATE_LIMIT_COOLDOWN_SECONDS", "1800")
)

_ddg_rate_limited_until = 0.0


def inter_isin_sleep() -> None:
    time.sleep(random.uniform(INTER_ISIN_SLEEP_MIN, INTER_ISIN_SLEEP_MAX))


def retrieve_factsheet(isin: str, download_dir: str = DOWNLOAD_DIR_DEFAULT) -> dict:
    os.makedirs(download_dir, exist_ok=True)

    if _should_use_duckduckgo():
        url = _search_duckduckgo(isin)
        if url:
            path = _download_pdf(url, isin, download_dir)
            if path:
                return _success(source="duckduckgo", pdf_url=url, local_path=path)

    url = _scrape_justetf(isin)
    if url:
        path = _download_pdf(url, isin, download_dir)
        if path:
            return _success(source="justetf", pdf_url=url, local_path=path)

    return _failure("Both retrieval levels failed to yield a valid PDF")


def _search_duckduckgo(isin: str) -> str | None:
    global _ddg_rate_limited_until

    query = f'{isin} "factsheet" filetype:pdf'
    try:
        with DDGS() as ddgs:
            results = list(ddgs.text(query, max_results=5))
        time.sleep(random.uniform(DDG_SLEEP_MIN, DDG_SLEEP_MAX))
        for r in results:
            href = r.get("href", "")
            if href.lower().endswith(".pdf"):
                return href
    except Exception as ex:
        if "ratelimit" in str(ex).lower():
            _ddg_rate_limited_until = time.time() + DDG_RATE_LIMIT_COOLDOWN_SECONDS
            print(
                "[retrieve] DuckDuckGo rate-limited; "
                f"cooldown for {int(DDG_RATE_LIMIT_COOLDOWN_SECONDS)}s"
            )
        time.sleep(random.uniform(DDG_SLEEP_MIN, DDG_SLEEP_MAX))
    return None


def _should_use_duckduckgo() -> bool:
    if not DDG_ENABLED:
        return False

    return time.time() >= _ddg_rate_limited_until


def _scrape_justetf(isin: str) -> str | None:
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
    try:
        resp = requests.get(url, headers=HEADERS, timeout=REQUEST_TIMEOUT, stream=True)
        resp.raise_for_status()

        safe_name = re.sub(r"[^A-Za-z0-9_-]", "_", isin)
        local_path = os.path.join(download_dir, f"{safe_name}_factsheet.pdf")

        with open(local_path, "wb") as f:
            for chunk in resp.iter_content(chunk_size=8192):
                f.write(chunk)

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

from __future__ import annotations

import os
import sys
from io import BytesIO
from unittest.mock import MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", ".."))

from airflow.include.transforms.factsheet_retrieval import (
    _download_pdf,
    _scrape_justetf,
    _search_duckduckgo,
    retrieve_factsheet,
)

FAKE_ISIN = "IE00B4L5Y983"
FAKE_PDF_URL = "https://cdn.example.com/factsheet.pdf"
FAKE_PDF_CONTENT = b"%PDF-1.4 fake content"
FAKE_HTML = b"<html><body>not a pdf</body></html>"


def _mock_pdf_response():
    resp = MagicMock()
    resp.raise_for_status = MagicMock()
    resp.iter_content = MagicMock(return_value=iter([FAKE_PDF_CONTENT]))
    return resp


def _mock_html_response(body: str):
    resp = MagicMock()
    resp.raise_for_status = MagicMock()
    resp.text = body
    return resp


class TestSearchDuckDuckGo:
    def test_returns_first_pdf_href(self):
        results = [
            {"href": "https://example.com/page.html"},
            {"href": FAKE_PDF_URL},
        ]
        with patch(
                "airflow.include.transforms.factsheet_retrieval.DDGS"
        ) as MockDDGS:
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = results
            result = _search_duckduckgo(FAKE_ISIN)
        assert result == FAKE_PDF_URL

    def test_returns_none_when_no_pdf_results(self):
        results = [{"href": "https://example.com/page.html"}]
        with patch(
                "airflow.include.transforms.factsheet_retrieval.DDGS"
        ) as MockDDGS:
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = results
            result = _search_duckduckgo(FAKE_ISIN)
        assert result is None

    def test_returns_none_on_exception(self):
        with patch(
                "airflow.include.transforms.factsheet_retrieval.DDGS"
        ) as MockDDGS:
            MockDDGS.return_value.__enter__.side_effect = Exception("rate limit")
            result = _search_duckduckgo(FAKE_ISIN)
        assert result is None


class TestScrapeJustEtf:
    def test_returns_absolute_factsheet_link(self):
        html = (
            '<html><body>'
            '<a href="/files/factsheet.pdf">Factsheet</a>'
            '</body></html>'
        )
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.return_value = _mock_html_response(html)
            result = _scrape_justetf(FAKE_ISIN)
        assert result == "https://www.justetf.com/files/factsheet.pdf"

    def test_returns_none_when_no_factsheet_link(self):
        html = '<html><body><a href="/overview.html">Overview</a></body></html>'
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.return_value = _mock_html_response(html)
            result = _scrape_justetf(FAKE_ISIN)
        assert result is None

    def test_returns_none_on_http_error(self):
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.side_effect = Exception("connection error")
            result = _scrape_justetf(FAKE_ISIN)
        assert result is None


class TestDownloadPdf:
    def test_returns_local_path_for_valid_pdf(self, tmp_path):
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.return_value = _mock_pdf_response()
            result = _download_pdf(FAKE_PDF_URL, FAKE_ISIN, str(tmp_path))
        assert result is not None
        assert result.endswith("_factsheet.pdf")
        assert os.path.exists(result)

    def test_returns_none_for_non_pdf_content(self, tmp_path):
        resp = MagicMock()
        resp.raise_for_status = MagicMock()
        resp.iter_content = MagicMock(return_value=iter([FAKE_HTML]))
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.return_value = resp
            result = _download_pdf(FAKE_PDF_URL, FAKE_ISIN, str(tmp_path))
        assert result is None

    def test_returns_none_on_request_exception(self, tmp_path):
        with patch(
                "airflow.include.transforms.factsheet_retrieval.requests.get"
        ) as mock_get:
            mock_get.side_effect = Exception("timeout")
            result = _download_pdf(FAKE_PDF_URL, FAKE_ISIN, str(tmp_path))
        assert result is None


class TestRetrieveFactsheet:
    def test_returns_downloaded_when_duckduckgo_yields_valid_pdf(self, tmp_path):
        with (
            patch("airflow.include.transforms.factsheet_retrieval.DDGS") as MockDDGS,
            patch("airflow.include.transforms.factsheet_retrieval.requests.get") as mock_get,
        ):
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = [{"href": FAKE_PDF_URL}]
            mock_get.return_value = _mock_pdf_response()

            result = retrieve_factsheet(FAKE_ISIN, download_dir=str(tmp_path))

        assert result["status"] == "downloaded"
        assert result["source"] == "duckduckgo"
        assert result["local_path"] is not None
        assert result["error"] is None

    def test_falls_back_to_justetf_when_duckduckgo_returns_no_results(self, tmp_path):
        justetf_html = (
            '<html><body>'
            '<a href="/files/factsheet.pdf">Factsheet</a>'
            '</body></html>'
        )
        with (
            patch("airflow.include.transforms.factsheet_retrieval.DDGS") as MockDDGS,
            patch("airflow.include.transforms.factsheet_retrieval.requests.get") as mock_get,
        ):
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = []

            html_resp = _mock_html_response(justetf_html)
            pdf_resp = _mock_pdf_response()
            mock_get.side_effect = [html_resp, pdf_resp]

            result = retrieve_factsheet(FAKE_ISIN, download_dir=str(tmp_path))

        assert result["status"] == "downloaded"
        assert result["source"] == "justetf"

    def test_returns_failed_when_both_levels_yield_nothing(self, tmp_path):
        html = '<html><body>no links</body></html>'
        with (
            patch("airflow.include.transforms.factsheet_retrieval.DDGS") as MockDDGS,
            patch("airflow.include.transforms.factsheet_retrieval.requests.get") as mock_get,
        ):
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = []
            mock_get.return_value = _mock_html_response(html)

            result = retrieve_factsheet(FAKE_ISIN, download_dir=str(tmp_path))

        assert result["status"] == "failed"
        assert result["source"] is None
        assert result["error"] is not None

    def test_rejects_non_pdf_content_even_if_url_ends_in_pdf(self, tmp_path):
        html_as_pdf_resp = MagicMock()
        html_as_pdf_resp.raise_for_status = MagicMock()
        html_as_pdf_resp.iter_content = MagicMock(return_value=iter([FAKE_HTML]))

        with (
            patch("airflow.include.transforms.factsheet_retrieval.DDGS") as MockDDGS,
            patch("airflow.include.transforms.factsheet_retrieval.requests.get") as mock_get,
        ):
            instance = MockDDGS.return_value.__enter__.return_value
            instance.text.return_value = [{"href": FAKE_PDF_URL}]
            mock_get.return_value = html_as_pdf_resp

            result = retrieve_factsheet(FAKE_ISIN, download_dir=str(tmp_path))

        assert result["status"] == "failed"
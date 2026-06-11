from __future__ import annotations

import httpx
import pdfplumber

CHUNK_SIZE_CHARS = 2000
OVERLAP_FRACTION = 0.12
OLLAMA_URL = "http://host.docker.internal:11434"
EMBEDDING_MODEL = "nomic-embed-text"


def extract_text_from_pdf(pdf_path: str) -> str:
    with pdfplumber.open(pdf_path) as pdf:
        pages = [page.extract_text() or "" for page in pdf.pages]
    return "\n".join(pages).strip()


def sliding_window_chunk(
        text: str,
        chunk_size: int = CHUNK_SIZE_CHARS,
        overlap_fraction: float = OVERLAP_FRACTION,
) -> list[str]:
    if not text:
        return []
    overlap = int(chunk_size * overlap_fraction)
    step = chunk_size - overlap
    chunks: list[str] = []
    for start in range(0, len(text), step):
        chunk = text[start : start + chunk_size].strip()
        if chunk:
            chunks.append(chunk)
        if start + chunk_size >= len(text):
            break
    return chunks


def generate_embedding(text: str, client: httpx.Client) -> list[float]:
    resp = client.post(
        f"{OLLAMA_URL}/api/embeddings",
        json={"model": EMBEDDING_MODEL, "prompt": text},
        timeout=60.0,
    )
    resp.raise_for_status()
    return resp.json()["embedding"]


def process_factsheet(
        ticker: str,
        pdf_path: str,
        client: httpx.Client,
) -> list[dict]:
    text = extract_text_from_pdf(pdf_path)
    if not text:
        raise ValueError(f"No text extracted from {pdf_path}")

    chunks = sliding_window_chunk(text)
    total = len(chunks)
    results: list[dict] = []
    for idx, chunk_text in enumerate(chunks):
        embedding = generate_embedding(chunk_text, client)
        results.append(
            {
                "content": chunk_text,
                "embedding": embedding,
                "chunkIndex": idx,
                "metadata": {
                    "source": "factsheet",
                    "pdfPath": pdf_path,
                    "chunkIndex": idx,
                    "totalChunks": total,
                },
            }
        )
    return results
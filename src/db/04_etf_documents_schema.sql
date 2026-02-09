
-- ETF Documents table to look into (ETF Descriptions, News, etc.)
CREATE TABLE IF NOT EXISTS etf_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticker VARCHAR(20) NOT NULL, -- FK logic toward etf_metadata
    content TEXT NOT NULL,       -- The raw text (es. "This ETF invests in AI companies...")
    metadata JSONB,              -- Metadati extra (es. source, data)
    embedding vector(768),      -- the mathematic "thought vector" of the text  (Ollama uses 768)
    created_at TIMESTAMP DEFAULT NOW(),
    is_mandatory BOOL DEFAULT False,

    CONSTRAINT fk_ticker_doc FOREIGN KEY (ticker) 
        REFERENCES etf_metadata(ticker) ON DELETE CASCADE
);

- Index HNSW (Hierarchical Navigable Small World) for fast search
-- With no index, vectorial seaerch is slow (sequential  scan)
CREATE INDEX IF NOT EXISTS idx_etf_documents_embedding
    ON etf_documents USING hnsw (embedding vector_cosine_ops);
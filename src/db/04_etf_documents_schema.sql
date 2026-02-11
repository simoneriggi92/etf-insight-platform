
-- ETF Documents table to look into (ETF Descriptions, News, etc.)
CREATE TABLE etf_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticker VARCHAR(20) NOT NULL UNIQUE,
    content TEXT NOT NULL,
    metadata JSONB,
    embedding vector(768),
    created_at TIMESTAMP DEFAULT NOW(),
    is_mandatory BOOL DEFAULT False,
    CONSTRAINT fk_ticker_doc FOREIGN KEY (ticker) 
        REFERENCES etf_metadata(ticker) ON DELETE CASCADE
);

CREATE INDEX idx_etf_documents_embedding 
    ON etf_documents USING hnsw (embedding vector_cosine_ops);

-- Create UNIQUE contraint on ticker
ALTER TABLE etf_documents ADD CONSTRAINT unique_ticker UNIQUE (ticker);


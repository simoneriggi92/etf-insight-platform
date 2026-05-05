CREATE TABLE IF NOT EXISTS etf_factsheet_status (
    isin        VARCHAR(12) NOT NULL PRIMARY KEY,
    ticker      VARCHAR(20) NOT NULL REFERENCES etf_metadata(ticker) ON DELETE CASCADE,
    status      VARCHAR(20) NOT NULL DEFAULT 'pending',
    source      VARCHAR(30),
    pdf_url     TEXT,
    local_path  TEXT,
    error       TEXT,
    attempts    INT NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS etf_factsheet_status_status ON etf_factsheet_status (status);
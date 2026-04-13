-- 09_broker_pdf_import.sql
-- Phase 1: Database foundations for Trade Republic broker PDF import.
-- Safe to re-run: all DDL uses IF NOT EXISTS / DO $$ BEGIN...EXCEPTION guards.

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.2  Enums
-- ─────────────────────────────────────────────────────────────────────────────

DO $$ BEGIN
    CREATE TYPE broker_import_job_status AS ENUM (
        'queued',
        'processing',
        'waiting_for_ingestion',
        'completed',
        'completed_with_errors',
        'failed'
    );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE TYPE broker_import_item_status AS ENUM (
        'queued',
        'parsing',
        'parsed',
        'duplicate',
        'unsupported',
        'unresolved_instrument',
        'waiting_for_ingestion',
        'imported',
        'failed'
    );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.3  broker_import_jobs
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS broker_import_jobs (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    portfolio_id                UUID NOT NULL REFERENCES portfolios(id) ON DELETE CASCADE,
    user_id                     UUID NOT NULL,
    broker                      VARCHAR(50) NOT NULL,
    status                      broker_import_job_status NOT NULL DEFAULT 'queued',
    hangfire_job_id             VARCHAR(50) NULL,
    total_files                 INT NOT NULL,
    processed_files             INT NOT NULL DEFAULT 0,
    imported_files              INT NOT NULL DEFAULT 0,
    duplicate_files             INT NOT NULL DEFAULT 0,
    failed_files                INT NOT NULL DEFAULT 0,
    waiting_for_ingestion_files INT NOT NULL DEFAULT 0,
    current_file_name           TEXT NULL,
    current_message             TEXT NULL,
    error_summary               TEXT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    started_at                  TIMESTAMPTZ NULL,
    completed_at                TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_broker_import_jobs_portfolio
    ON broker_import_jobs(portfolio_id);

CREATE INDEX IF NOT EXISTS idx_broker_import_jobs_user
    ON broker_import_jobs(user_id);

CREATE INDEX IF NOT EXISTS idx_broker_import_jobs_status
    ON broker_import_jobs(status);

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.4  broker_import_job_items
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS broker_import_job_items (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id                      UUID NOT NULL REFERENCES broker_import_jobs(id) ON DELETE CASCADE,
    portfolio_id                UUID NOT NULL REFERENCES portfolios(id) ON DELETE CASCADE,
    original_file_name          TEXT NOT NULL,
    temp_file_path              TEXT NOT NULL,
    file_sha256                 CHAR(64) NOT NULL,
    status                      broker_import_item_status NOT NULL DEFAULT 'queued',
    broker_reference            VARCHAR(100) NULL,
    broker_secondary_reference  VARCHAR(100) NULL,
    isin                        VARCHAR(12) NULL,
    instrument_name             TEXT NULL,
    resolved_ticker             VARCHAR(20) NULL,
    transaction_type            VARCHAR(20) NULL,
    transaction_date            DATE NULL,
    settlement_date             DATE NULL,
    units                       NUMERIC(18, 8) NULL,
    price_per_unit              NUMERIC(18, 8) NULL,
    fees                        NUMERIC(18, 8) NULL,
    gross_amount                NUMERIC(18, 8) NULL,
    currency                    VARCHAR(3) NULL,
    created_transaction_id      UUID NULL,
    error_message               TEXT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_broker_import_items_job
    ON broker_import_job_items(job_id);

CREATE INDEX IF NOT EXISTS idx_broker_import_items_file_hash
    ON broker_import_job_items(file_sha256);

CREATE INDEX IF NOT EXISTS idx_broker_import_items_isin
    ON broker_import_job_items(isin)
    WHERE isin IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_broker_import_items_status
    ON broker_import_job_items(status);

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.5  etf_metadata: partial unique index on ISIN
-- ─────────────────────────────────────────────────────────────────────────────
-- name is already VARCHAR(200) and ticker is already VARCHAR(20) — no changes needed.

-- Resolve duplicate ISINs before creating the unique index.
-- Rows with transactions cannot be deleted (FK constraint), so we clear the isin
-- on the non-canonical duplicates instead. The canonical row is the one with the
-- most recent ingestion_requested_at, falling back to earliest created_at.
UPDATE etf_metadata
SET isin = NULL
WHERE isin IS NOT NULL
  AND ctid NOT IN (
      SELECT DISTINCT ON (isin) ctid
      FROM etf_metadata
      WHERE isin IS NOT NULL
      ORDER BY isin, ingestion_requested_at DESC NULLS LAST, created_at ASC NULLS LAST
  );

CREATE UNIQUE INDEX IF NOT EXISTS uq_etf_metadata_isin
    ON etf_metadata(isin)
    WHERE isin IS NOT NULL;

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.6  transactions: provenance columns
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS source_broker               VARCHAR(50)  NULL,
    ADD COLUMN IF NOT EXISTS source_reference            VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS source_secondary_reference  VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS source_document_hash        CHAR(64)     NULL,
    ADD COLUMN IF NOT EXISTS source_isin                 VARCHAR(12)  NULL,
    ADD COLUMN IF NOT EXISTS trade_currency              VARCHAR(3)   NULL;

-- Idempotency: prevent duplicate import if same PDFs are uploaded again.
-- Primary key: (portfolio, broker, execution reference)
CREATE UNIQUE INDEX IF NOT EXISTS uq_transactions_broker_reference
    ON transactions(portfolio_id, source_broker, source_reference)
    WHERE source_broker IS NOT NULL AND source_reference IS NOT NULL;

-- Fallback key: (portfolio, broker, document hash)
CREATE UNIQUE INDEX IF NOT EXISTS uq_transactions_broker_document_hash
    ON transactions(portfolio_id, source_broker, source_document_hash)
    WHERE source_broker IS NOT NULL AND source_document_hash IS NOT NULL;

-- ─────────────────────────────────────────────────────────────────────────────
-- 7.7  transactions: increase numeric precision
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE transactions
    ALTER COLUMN units          TYPE NUMERIC(18, 8),
    ALTER COLUMN price_per_unit TYPE NUMERIC(18, 8),
    ALTER COLUMN fees           TYPE NUMERIC(18, 8);
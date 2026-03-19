-- 08_etf_ingestion_status.sql
-- Adds ingestion lifecycle tracking to etf_metadata.
-- Safe to re-run: all operations use IF NOT EXISTS / DO NOTHING guards.

-- 1. Lifecycle enum
DO $$ BEGIN
    CREATE TYPE etf_ingestion_status AS ENUM (
        'unknown',    -- ticker seen for first time, no data yet
        'pending',    -- queued, Airflow DAG not yet started
        'ingesting',  -- DAG is running
        'ready',      -- prices loaded, available for analytics
        'error'       -- DAG failed; retry needed
    );
EXCEPTION
    WHEN duplicate_object THEN NULL;  -- idempotent re-run
END $$;

-- 2. Add columns to etf_metadata
ALTER TABLE etf_metadata
    ADD COLUMN IF NOT EXISTS status                 etf_ingestion_status NOT NULL DEFAULT 'unknown',
    ADD COLUMN IF NOT EXISTS ingestion_requested_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS ingestion_completed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS ingestion_error        TEXT;

-- 3. Backfill: all currently active tickers already have prices → mark as ready
UPDATE etf_metadata
SET status = 'ready',
    ingestion_completed_at = last_sync
WHERE is_active = TRUE;

-- 4. Index to speed up status polling
CREATE INDEX IF NOT EXISTS idx_etf_metadata_status ON etf_metadata(status);
-- 07_multi_tenancy.sql
-- Adds guest-token multi-tenancy to portfolios.
-- Safe to re-run: all operations use IF NOT EXISTS / IF EXISTS guards.

-- 1. Add owner column (nullable — preserves existing seed rows)
ALTER TABLE portfolios
    ADD COLUMN IF NOT EXISTS user_id UUID;

CREATE INDEX IF NOT EXISTS idx_portfolios_user_id ON portfolios(user_id);

-- 2. Row-Level Security (belt-and-suspenders on top of app-layer filtering)
ALTER TABLE portfolios ENABLE ROW LEVEL SECURITY;
ALTER TABLE portfolios FORCE ROW LEVEL SECURITY;

-- Policy: sessions may only see their own portfolios.
-- The app sets app.user_id via set_config() before every query.
-- Superuser connections bypass RLS automatically.
DROP POLICY IF EXISTS portfolios_tenant_isolation ON portfolios;
CREATE POLICY portfolios_tenant_isolation ON portfolios
    USING (
        user_id IS NULL                                      -- legacy seed rows visible to all (dev only)
        OR user_id = current_setting('app.user_id', true)::uuid
    );
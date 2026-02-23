-- Portfolios table
CREATE TABLE IF NOT EXISTS portfolios
(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    currency VARCHAR(3) DEFAULT 'EUR',
    created_at TIMESTAMP DEFAULT NOW());

-- Transactions table 
CREATE TABLE IF NOT EXISTS transactions
(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    portfolio_id UUID NOT NULL REFERENCES portfolios(id) ON DELETE CASCADE,
    ticker VARCHAR(20) NOT NULL,
    transaction_date DATE NOT NULL,
    type VARCHAR(10) NOT NULL CHECK (type IN ('BUY', 'SELL','DEPOSIT','WITHDRAW')),
    units NUMERIC(18, 4) NOT NULL CHECK (units > 0),
    price_per_unit NUMERIC(18, 4) NOT NULL CHECK (price_per_unit > 0),
    fees  NUMERIC (18, 4) DEFAULT 0,

    CONSTRAINT fk_ticker
        FOREIGN KEY (ticker)
        REFERENCES etf_metadata (ticker) ON DELETE RESTRICT
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio ON transactions(portfolio_id);
CREATE INDEX IF NOT EXISTS idx_transactions_ticker ON transactions(ticker);
CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(transaction_date DESC);
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio_date ON transactions(portfolio_id, transaction_date);

-- Composite index for common queries
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio_ticker_date 
ON transactions(portfolio_id, ticker, transaction_date);

-- Seed Data
-- ─────────────────────────────────────────────────────────────────────────────
-- Demo seed: 2 portfolios + realistic transactions (2024-01-15 → 2026-02-23)
-- Safe to re-run: uses INSERT ... ON CONFLICT DO NOTHING
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- ── 1. Portfolios ─────────────────────────────────────────────────────────────

INSERT INTO portfolios (id, name, currency, created_at) VALUES
(
    'a1000000-0000-0000-0000-000000000001',
    'Growth ETF Portfolio',
    'USD',
    '2024-01-15 08:00:00+00'
),
(
    'a2000000-0000-0000-0000-000000000002',
    'Conservative ETF Portfolio',
    'USD',
    '2024-02-01 08:00:00+00'
)
ON CONFLICT (id) DO NOTHING;


-- ── 2. Portfolio 1 — Growth ETF Portfolio ────────────────────────────────────
--      High-growth tech-heavy ETFs + individual tech stocks
--      DEPOSIT rows use SPY as ticker placeholder (fk_ticker constraint)

INSERT INTO transactions
    (portfolio_id, ticker, transaction_date, type, units, price_per_unit, fees)
VALUES

-- 2024-01: Initial deployment (DEPOSIT — no ticker fk, use first BUY ticker)
('a1000000-0000-0000-0000-000000000001', 'QQQ',  '2024-01-15', 'DEPOSIT',  1, 50000.00, 0),
('a1000000-0000-0000-0000-000000000001', 'QQQ',  '2024-01-16', 'BUY',     45,   408.50, 4.95),
('a1000000-0000-0000-0000-000000000001', 'MSFT', '2024-01-16', 'BUY',     20,   374.00, 4.95),

-- 2024-02: Add NVDA ahead of earnings
('a1000000-0000-0000-0000-000000000001', 'NVDA', '2024-02-10', 'BUY',     30,   613.00, 4.95),

-- 2024-03: Add AAPL on dip
('a1000000-0000-0000-0000-000000000001', 'AAPL', '2024-03-05', 'BUY',     25,   169.00, 4.95),

-- 2024-06: Rotate MSFT → VGT
('a1000000-0000-0000-0000-000000000001', 'MSFT', '2024-06-20', 'SELL',    20,   445.00, 4.95),
('a1000000-0000-0000-0000-000000000001', 'VGT',  '2024-06-21', 'BUY',     28,   570.00, 4.95),

-- 2024-09: Speculative add SMCI
('a1000000-0000-0000-0000-000000000001', 'SMCI', '2024-09-15', 'BUY',     15,   420.00, 4.95),

-- 2025-01: Annual top-up + add to QQQ
('a1000000-0000-0000-0000-000000000001', 'QQQ',  '2025-01-10', 'DEPOSIT',  1, 20000.00, 0),
('a1000000-0000-0000-0000-000000000001', 'QQQ',  '2025-01-11', 'BUY',     20,   510.00, 4.95),

-- 2025-03: Double down NVDA post-GTC
('a1000000-0000-0000-0000-000000000001', 'NVDA', '2025-03-20', 'BUY',     20,   875.00, 4.95),

-- 2025-06: Cut SMCI loss, rotate to TSLA
('a1000000-0000-0000-0000-000000000001', 'SMCI', '2025-06-01', 'SELL',    15,   290.00, 4.95),
('a1000000-0000-0000-0000-000000000001', 'TSLA', '2025-06-05', 'BUY',     30,   245.00, 4.95),

-- 2026-01: YTD opening — add SPY hedge
('a1000000-0000-0000-0000-000000000001', 'SPY',  '2026-01-05', 'BUY',     15,   580.00, 4.95);


-- ── 3. Portfolio 2 — Conservative ETF Portfolio ───────────────────────────────
--      Diversified: broad market + bonds + gold + dividend

INSERT INTO transactions
    (portfolio_id, ticker, transaction_date, type, units, price_per_unit, fees)
VALUES

-- 2024-02: Initial deployment
('a2000000-0000-0000-0000-000000000002', 'SPY',  '2024-02-01', 'DEPOSIT',  1, 80000.00, 0),
('a2000000-0000-0000-0000-000000000002', 'SPY',  '2024-02-02', 'BUY',     60,   490.00, 4.95),
('a2000000-0000-0000-0000-000000000002', 'BND',  '2024-02-02', 'BUY',    180,    72.50, 4.95),

-- 2024-04: Add total market
('a2000000-0000-0000-0000-000000000002', 'VTI',  '2024-04-10', 'BUY',     45,   238.00, 4.95),

-- 2024-07: Gold hedge
('a2000000-0000-0000-0000-000000000002', 'GLD',  '2024-07-15', 'BUY',     40,   218.00, 4.95),

-- 2025-01: Annual rebalancing top-up
('a2000000-0000-0000-0000-000000000002', 'VTI',  '2025-01-05', 'DEPOSIT',  1, 15000.00, 0),
('a2000000-0000-0000-0000-000000000002', 'VTI',  '2025-01-06', 'BUY',     20,   285.00, 4.95),

-- 2025-03: Add dividend layer
('a2000000-0000-0000-0000-000000000002', 'SCHD', '2025-03-10', 'BUY',     60,   285.00, 4.95),

-- 2025-07: Rotate BND → AGG
('a2000000-0000-0000-0000-000000000002', 'BND',  '2025-07-15', 'SELL',   180,    74.20, 4.95),
('a2000000-0000-0000-0000-000000000002', 'AGG',  '2025-07-16', 'BUY',    175,    96.50, 4.95),

-- 2026-01: YTD add to SPY
('a2000000-0000-0000-0000-000000000002', 'SPY',  '2026-01-10', 'BUY',     10,   580.00, 4.95);

COMMIT;
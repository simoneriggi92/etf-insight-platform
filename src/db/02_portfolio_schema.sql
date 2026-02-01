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
INSERT INTO portfolios (id, name) VALUES ('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'Pension Fund');
INSERT INTO transactions (portfolio_id, ticker, transaction_date, type, units, price_per_unit, fees)
VALUES 
('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'EUNL.DE', '2023-01-10', 'BUY', 10, 75.50, 2.50),
('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'EUNA.DE', '2023-02-15', 'BUY', 50, 4.20, 1.00);
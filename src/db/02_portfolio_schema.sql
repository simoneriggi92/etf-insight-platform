-- Portfolios table
CREATE TABLE IF NOT EXISTS portfolios
(
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    base_currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Transactions table 
CREATE TABLE IF NOT EXISTS transactions
(
    id SERIAL PRIMARY KEY,
    portfolio_id INT NOT NULL REFERENCES portfolios(id) ON DELETE CASCADE,
    symbol VARCHAR(10) NOT NULL,
    transaction_type VARCHAR(10) NOT NULL CHECK (transaction_type IN ('BUY', 'SELL')),
    quantity DECIMAL(12, 6) NOT NULL CHECK (quantity > 0),
    price DECIMAL(12, 4) NOT NULL CHECK (price > 0),
    transaction_date DATE NOT NULL DEFAULT NOW(),
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),

    CONSTRAINT fk_portfolio
        FOREIGN KEY (portfolio_id)
        REFERENCES portfolios (id)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio ON transactions(portfolio_id);
CREATE INDEX IF NOT EXISTS idx_transactions_symbol ON transactions(symbol);
CREATE INDEX IF NOT EXISTS idx_transactions_date ON transactions(transaction_date DESC);
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio_date ON transactions(portfolio_id, transaction_date);

-- Composite index for common queries
CREATE INDEX IF NOT EXISTS idx_transactions_portfolio_symbol_date 
ON transactions(portfolio_id, symbol, transaction_date);
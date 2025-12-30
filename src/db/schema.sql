CREATE TABLE
IF NOT EXISTS etf_prices
(
    id SERIAL PRIMARY KEY,
    symbol VARCHAR
(10) NOT NULL,
    price_date DATE NOT NULL,
    open_price DECIMAL
(12, 4),
    high_price DECIMAL
(12, 4),
    low_price DECIMAL
(12, 4),
    close_price DECIMAL
(12, 4),
    volume BIGINT,
    created_at TIMESTAMP DEFAULT NOW
(),
    UNIQUE
(symbol, price_date)
);

-- Index for common queries
CREATE INDEX
IF NOT EXISTS idx_etf_prices_symbol_date 
ON etf_prices
(symbol, price_date DESC);

-- Index for date range queries
CREATE INDEX
IF NOT EXISTS idx_etf_prices_date 
ON etf_prices
(price_date DESC);
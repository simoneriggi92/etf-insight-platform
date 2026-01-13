-- Foreign exchange rates table

CREATE TABLE IF NOT EXISTS fx_rates (
    id SERIAL PRIMARY KEY,
    rate_date DATE NOT NULL, 
    from_currency VARCHAR(3) NOT NULL,
    to_currency VARCHAR(3) NOT NULL,
    rate DECIMAL (12,6) NOT NULL CHECK (rate > 0),
    source VARCHAR(50) DEFAULT 'ECB',
    created_at TIMESTAMP DEFAULT NOW(),

    -- Ensure one rate per currency pair per date 
    UNIQUE(rate_date, from_currency, to_currency)
);

-- Indexes for efficient lookups
CREATE INDEX IF NOT EXISTS idx_fx_rates_date ON fx_rates (rate_date DESC);
CREATE INDEX IF NOT EXISTS idx_fx_rates_currencies ON fx_rates(from_currency, to_currency);
CREATE INDEX IF NOT EXISTS idx_fx_rates_lookup ON fx_rates(from_currency, to_currency, rate_date DESC);

-- Comments for documentation
COMMENT ON TABLE fx_rates IS 'Foreign exchange rates from ECB. Rate convention: 1 from_currency = rate to_currency':
COMMENT ON COLUMN fx_rates.rate IS 'Exchange rate: amount of to_currency per 1 unit of from_currency';

-- Example: EUR/USD rate of 1.10 means 1 EUR = 1.10 USD
-- To convert: amount_usd = amount_eur * rate



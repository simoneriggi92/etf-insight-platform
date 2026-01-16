-- Track the original transaction currency without computing FX P&L
ALTER TABLE etf_prices
ADD COLUMN currency VARCHAR(3) DEFAULT 'USD' NOT NULL;

-- Backfill existing data
UPDATE transactions SET currency = 'USD';
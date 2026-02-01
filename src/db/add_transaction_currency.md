-- Track the original transaction currency without computing FX P&L
ALTER TABLE transactions
ADD COLUMN transaction_currency VARCHAR(3) DEFAULT 'USD' NOT NULL;

-- Backfill existing data
UPDATE transactions SET transaction_currency = 'USD';
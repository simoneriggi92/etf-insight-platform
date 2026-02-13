-- Shadow table for the history

CREATE TABLE IF NOT EXISTS etf_prices_audit(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticker VARCHAR(20) NOT NULL,
    price_date DATE NOT NULL,
    old_close_price NUMERIC(18, 4),
    new_close_price NUMERIC(18, 4),
    change_type VARCHAR(10), -- 'UPDATE', 'DELETE'
    changed_at TIMESTAMP DEFAULT NOW(),
    changed_by VARCHAR(50) DEFAULT 'system'
);

-- TRIGGER FUNCTION
CREATE OR REPLACE FUNCTION log_price_changes()
RETURNS TRIGGER AS $$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        INSERT INTO etf_prices_audit (ticker, price_date, old_close_price, change_type)
        VALUES (OLD.ticker, OLD.price_date, OLD.close_price, 'DELETE');
        RETURN OLD;
    ELSIF (TG_OP = 'UPDATE') THEN
        -- LLogs only if the price really changes
        IF OLD.close_price <> NEW.close_price THEN
            INSERT INTO etf_prices_audit (ticker, price_date, old_close_price, new_close_price, change_type)
            VALUES (OLD.ticker, OLD.price_date, OLD.close_price, NEW.close_price, 'UPDATE');
        END IF;
        RETURN NEW;
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- Trigger activation
DROP TRIGGER IF EXISTS trg_audit_prices ON etf_prices;
CREATE TRIGGER trg_audit_prices
AFTER UPDATE OR DELETE ON etf_prices
FOR EACH ROW
EXECUTE FUNCTION log_price_changes();
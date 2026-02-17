-- Table for data quality anomalies detection
CREATE TABLE IF NOT EXISTS public.data_anomalies(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ticker VARCHAR(20) NOT NULL,
    price_date DATE NOT NULL,
    rule_name VARCHAR(100) NOT NULL,
    severity VARCHAR(20) NOT NULL, -- 'WARNING', 'ERROR', 'CRITICAL'
    current_value NUMERIC(18, 4),
    expected_range VARCHAR(100),
    message TEXT,
    metadata JSONB, -- Additional context (e.g., previous price, threshold)
    detected_at TIMESTAMP DEFAULT NOW(),
    resolved BOOLEAN DEFAULT FALSE,
    resolved_at TIMESTAMP,
    resolved_by VARCHAR(50)
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_data_anomalies_ticker_date
    ON public.data_anomalies USING btree
    (ticker, price_date DESC);

CREATE INDEX IF NOT EXISTS idx_data_anomalies_detected_at
    ON public.data_anomalies USING btree
    (detected_at DESC);

CREATE INDEX IF NOT EXISTS idx_data_anomalies_unresolved
    ON public.data_anomalies USING btree
    (resolved, detected_at DESC)
    WHERE resolved = FALSE;

ALTER TABLE IF EXISTS public.data_anomalies
    OWNER to etfinsight;
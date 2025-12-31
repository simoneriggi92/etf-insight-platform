-- Total records
SELECT COUNT(*)
FROM etf_prices;
-- Expected: ~1,512 (or 1,507 + 15 = 1,522 with yesterday's data)

-- Records per symbol
SELECT symbol, COUNT(*) as trading_days,
    MIN(price_date) as first_date,
    MAX(price_date) as last_date
FROM etf_prices
GROUP BY symbol
ORDER BY symbol;
-- Expected output:
--  symbol | trading_days | first_date | last_date  
-- --------+--------------+------------+------------
--  QQQ    |          509 | 2023-01-03 | 2025-12-30
--  SPY    |          509 | 2023-01-03 | 2025-12-30
--  VTI    |          509 | 2023-01-03 | 2025-12-30

-- Check for gaps (dates where we should have data but don't)
-- This query finds the gap between consecutive trading days
WITH
    date_gaps
    AS
    (
        SELECT symbol, price_date,
            LEAD(price_date) OVER (PARTITION BY symbol ORDER BY price_date) as next_date,
            LEAD(price_date) OVER (PARTITION BY symbol ORDER BY price_date) - price_date as gap_days
        FROM etf_prices
    )
SELECT symbol, price_date, next_date, gap_days
FROM date_gaps
WHERE gap_days > 5
-- Gaps longer than weekend + holiday
ORDER BY symbol, price_date;
-- Should be empty or only show known long weekends/holidays

-- Price sanity check (detect anomalies)
SELECT symbol, price_date, close_price,
    LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date) as prev_close,
    ROUND((close_price - LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date)) / 
             LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date) * 100, 2) as pct_change
FROM etf_prices
WHERE ABS((close_price - LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date)) / 
          LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date) * 100) > 10
ORDER BY ABS(pct_change) DESC;
-- Should be empty or only show known market crashes/splits

-- Volume sanity check
SELECT symbol, price_date, volume
FROM etf_prices
WHERE volume = 0 OR volume IS NULL
ORDER BY symbol, price_date;
-- Should be mostly empty (some ETFs might have 0 volume on holidays)
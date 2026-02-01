-- Daily price changes
SELECT symbol, price_date,
    close_price,
    close_price - LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date) as daily_change,
    ROUND((close_price - LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date)) / 
             LAG(close_price) OVER (PARTITION BY symbol ORDER BY price_date) * 100, 2) as pct_change
FROM etf_prices
ORDER BY symbol, price_date;

-- Intraday volatility
SELECT symbol, price_date,
    ROUND(((high_price - low_price) / open_price * 100), 2) as intraday_volatility_pct
FROM etf_prices
ORDER BY intraday_volatility_pct DESC;

-- Volume trends
SELECT symbol,
    MIN(volume) as min_vol,
    MAX(volume) as max_vol,
    ROUND(AVG(volume)) as avg_vol
FROM etf_prices
GROUP BY symbol;
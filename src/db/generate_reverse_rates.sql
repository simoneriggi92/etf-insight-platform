-- Insert inverse rates (rate_date, from_currency, to_currency, rate, source)

INSERT INTO fx_rates(rate_date, from_currency, to_currency, rate, source)
SELECT
    rate_date,
    to_currency as from_currency,
    from_currency as to_currency,
    1.0 / rate as rate,
    source || '_inverse' as source
FROM
    fx_rates
WHERE 
    source = 'ECB'
ON CONFLICT (rate_date, from_currency, to_currency) DO NOTHING;

-- Verify reverse rates 
SELECT
    count(*) as total_rates,
    count(DISTINCT from_currency || '/' || to_currency) as unique_pairs
FROM 
    fx_rates;

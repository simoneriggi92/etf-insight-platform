- Clear existing data (for clean reruns)
TRUNCATE TABLE transactions CASCADE;
TRUNCATE TABLE portfolios RESTART IDENTITY CASCADE;

-- Create sample portfolio
INSERT INTO portfolios (name, description, base_currency) 
VALUES ('Tech Growth Portfolio', 'Long-term tech ETF holdings', 'USD')
RETURNING id;

-- Add transactions (adjust portfolio_id if needed)
-- Buy QQQ
INSERT INTO transactions (portfolio_id, symbol, transaction_type, quantity, price, transaction_date, notes)
VALUES 
(1, 'QQQ', 'BUY', 50, 380.25, '2023-01-15', 'Initial position'),
(1, 'QQQ', 'BUY', 25, 420.50, '2023-06-01', 'Add to position'),
(1, 'SPY', 'BUY', 30, 450.75, '2023-03-10', 'Diversification'),
(1, 'VTI', 'BUY', 100, 210.30, '2023-02-20', 'Broad market exposure'),
(1, 'QQQ', 'SELL', 15, 520.00, '2024-11-15', 'Partial profit taking');

-- Verify
SELECT p.name, COUNT(t.id) as transaction_count
FROM portfolios p
LEFT JOIN transactions t ON p.id = t.portfolio_id
GROUP BY p.id, p.name;

SELECT * FROM transactions ORDER BY transaction_date;
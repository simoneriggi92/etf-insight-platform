insert into etf (ticker, name, currency, provider) values
('VWCE', 'Vanguard FTSE All-World UCITS ETF', 'EUR', 'Vanguard'),
('IWDA', 'iShares Core MSCI World UCITS ETF', 'EUR', 'iShares'),
('EIMI', 'iShares Core MSCI EM IMI UCITS ETF', 'EUR', 'iShares');

insert into portfolio_transaction
	(portfolio_id, etf_id, trade_date, trade_type, quantity, total_amount, notes, created_at)
values
	(1, 1, '2025-11-21', 'BUY', 1, 111.7, 'VWCE buy', now()),
	(1, 2, '2025-11-21', 'BUY', 2, 171.8, 'IWDA buy', now()),
	(1, 3, '2025-11-21', 'SELL', 1, 30.4, 'EIMI sell', now())
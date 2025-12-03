-- Quantita' per ETF in un portafoglio
select 
	pt.portfolio_id,
	pt.etf_id,
	e.ticker,
	sum(case when pt.trade_type = 'BUY' then pt.quantity else -pt.quantity end) as quantity
	from portfolio_transaction pt
	join etf e on e.id = pt.etf_id
	where pt.portfolio_id = 1
	group by pt.portfolio_id, pt.etf_id, e.ticker;
	-- Valore totale alla data D usanndo close_price
	
	with positions as (
		select 
			pt.portfolio_id,
			pt.etf_id, 
			sum(case when pt.trade_type = 'BUY' then pt.quantity else -pt.quantity end) as quantity
		from portfolio_transaction  pt
		where pt.portfolio_id = 1
		and pt.trade_date <= date '2025-11-21'
		group by pt.portfolio_id, pt.etf_id
	),
	prices as (
    select
        e.id as etf_id,
        e.ticker,
        eph.price_date,
        eph.close_price
    from etf_price_history eph
    join etf e on e.id = eph.etf_id
    where eph.price_date = date '2025-11-21'
)
select 
	p.portfolio_id,
    pr.price_date,
    pr.ticker,
    p.quantity,
    pr.close_price,
    (p.quantity * pr.close_price) as position_value
from positions p
join prices pr on pr.etf_id = p.etf_id;
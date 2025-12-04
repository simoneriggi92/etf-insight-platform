-- =========
-- Core ETF Schema v0
-- =========

create table if not exists etf (
    id serial primary key, 
    ticker varchar(10) not null unique,
    name varchar(255) not null,
    currency varchar(10) not null,
    provider varchar(255) null
);


create table if not exists etf_price_history (
    id serial primary key,
    etf_id integer references etf(id) on delete cascade,
    price_date date not null,
    open_price numeric(20,10) not null,
    close_price numeric(20,10) not null,
    high_price numeric(20,10) not null,
    low_price numeric(20,10) not null,
    volume bigint not null,
    CONSTRAINT uq_etf_price_history_etf_price_date UNIQUE (etf_id, price_date) -- Unique constraint enforces idempotency (no duplicate rows for the same ETF and date). An index is automatically created for unique constraints.
);

-- Index on date supports time-based queries (e.g., last N days).
create index if not exists idx_etf_price_history_price_date 
  on etf_price_history (price_date);

-- =========
-- Portfolio Schema v0
-- =========

create table if not EXISTS portfolio (
    id serial primary key,
    name varchar(200) not null,
    base_currency varchar(10) not null,
    created_at timestamp without time zone default now() not null
);

create table if not EXISTS portfolio_transaction(
    id bigserial primary key,
    portfolio_id integer not null references portfolio(id) on delete cascade,
    etf_id integer not null references etf(id) on delete restrict,
    trade_date date not null,
    trade_type varchar(10) not null, -- e.g., 'BUY' or 'SELL'
    quantity numeric(20,10) not null,
    total_amount numeric(20,10) not null, -- in portfolio's base currency
    notes varchar(500) null,
    created_at timestamp without time zone default now() not null
);

create index if not exists idx_portfolio_transaction_portfolio_date
  on portfolio_transaction (portfolio_id, trade_date);

create index if not exists idx_portfolio_transaction_etf
  on portfolio_transaction (etf_id);


-- =========
-- Portfolio Storicization Schema v0
-- =========

create table if not EXISTS portfolio_valuation
(
    id bigserial primary key,
    portfolio_id integer not null references portfolio(id) on delete cascade,
    valuation_date date not null,
    total_value numeric(20,10) not null, -- in portfolio's base currency
    created_at timestamp without time zone default now() not null,
    CONSTRAINT uq_portfolio_valuation UNIQUE (portfolio_id, valuation_date) -- Unique constraint to avoid duplicate valuations for the same date
)

create index if not exists idx_portfolio_valuation_portfolio_date
  on portfolio_valuation (portfolio_id, valuation_date);
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


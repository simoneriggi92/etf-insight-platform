-- Migration: allow multiple chunks per ticker in etf_documents
alter table etf_documents DROP CONSTRAINT IF EXISTS etf_documents_ticker_key;

alter table etf_documents add column if not exists chunk_index INT NOT NULL DEFAULT 0;
alter table etf_documents add column if not exists source varchar(50) not null default 'manual seed';

alter table etf_documents add constraint 
    uq_etf_documents_ticker_chuck unique (ticker, chunk_index);

# etf-insight-platform

## Overview
etf-insight-platform is a data & analytics platform for ETC/PAC portfolios.
Its goal is to ingest ETF data (prices, metadata, benchmarks) and investor transactions, compute meaningful portfolio metrics over time, and provide explainations and insights through an AI layer.

## Target user
The primary target user is a retail investor who:
- Invests mainly through PACs and ETFs,
- wants a long-term, data-driven view of their portfolio,
- needs clear explainations about performance, risk and allocation changes, not just charts.

Initially the only user is the developer (me).
The platform is designed so that it can later evolve into a multi-user, SaaS-style product.

## Why this project matter
Most retail investors with PACs and ETFs face the same problems:
- Their portfolio data is scattered across multiple brokers and sources.
- They see charts and percentages, but not *why* things are moving.
- They have no structured way to measure risk, exposure and long-term progress.
- They have to interpret complex documentation (ETF factsheets, KIDs) on their own.

This platform focuses on:
- **Data ingestion & modeling**
    - Automatic collection of price history and ETF metadata
    - Modeling of portfolios, positions and transactions

- **Portfolio analytics**
    - Computation of key metrics (performances, allocations, contributions, drawdowns, etc.).
    - Analysis of portfolio evolution over time.

_ **AI-powered understanding**
    - Generation of human-readable reports (monthly/yearly digests).
    - Q&A both portfolio data and ETF documentation through an AI layer (RAG).

The technical goal is to act as a long-term lab for data-engineering, backend architecture and AI integration on a real, non-trivial domain.

## Roadmap / Phases

- **Phase 0–1 – Core data & portfolio modeling**
  - Ingestion of ETF prices and metadata.
  - Basic portfolio & transaction model.
  - Computation of fundamental metrics and time–series.

- **Phase 2 – AI reporting**
  - Monthly/periodic reports explaining portfolio changes and contributions.
  - Event-based explanations (e.g. large drawdowns, big shifts in allocation).

- **Phase 3 – RAG on ETF documentation**
  - Ingestion and chunking of ETF documents (factsheets, descriptions, KIDs where possible).
  - Semantic search and Q&A about ETFs and portfolio using RAG.

- **Phase 4 – Evolved architecture & potential SaaS**
  - More robust, modular architecture (separate services, messaging, observability).
  - Multi-tenant capabilities and SaaS hardening if there is real demand.
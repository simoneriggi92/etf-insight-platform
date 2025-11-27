# Vision

## Problem

Long–term retail investors who build PACs with ETFs face a recurring set of issues:

- **Fragmented data**
  - Portfolio data is split across multiple brokers, banking apps and spreadsheets.
  - There is no single, consistent view of positions, transactions and history.

- **Poor understanding of performance**
  - They see % gains/losses but do not clearly understand:
    - what portion comes from contributions vs. market moves,
    - which ETFs are driving performance,
    - how the portfolio behaves across different market regimes.

- **Limited view of risk and exposure**
  - It is hard to see how the portfolio is exposed by region, sector, currency, factor, etc.
  - Rebalancing needs and concentration risks are not obvious.

- **Complex documentation**
  - ETF factsheets, KIDs and official documents are technical and time-consuming to read.
  - There is no assistant that can connect “what the documents say” with “what is happening in my portfolio”.

As a result, the investor flies half-blind: they keep investing, but without a deep, structured understanding of what they own and why the portfolio behaves as it does.

## Solution

Build a data platform that:

- **Ingests and normalizes ETF data**
  - Prices, basic metadata and (where possible) composition and benchmark information.
- **Models portfolios and transactions**
  - Supports PAC–like recurring contributions and manual transactions.
  - Computes daily valuations of holdings and portfolio over time.

- **Calculates key metrics**
  - Performance decomposition (contributions vs. market moves).
  - Allocations by ETF, region, sector, currency, etc.
  - Basic risk and drawdown metrics.

- **Uses AI to make the portfolio understandable**
  - Generates human-readable reports (monthly, yearly, event-based).
  - Answers questions about:
    - portfolio performance and risks,
    - individual ETFs characteristics,
    - documentation content (via RAG).

The platform is first and foremost a long–term personal lab to become extremely strong in data engineering, backend architecture and AI integration on a real financial domain. If the value proves real for other users, it can later evolve into a SaaS offering.

## Out of scope (for now)

- Direct trading or brokerage integration.
- Providing investment advice or recommendations ("buy X", "sell Y").
- Real-time high-frequency trading or intraday strategies.

The focus is on **analytics, understanding and decision support**, not on executing trades.

## Phases (high-level)

- **Phase 0–1 – Ingestion & basic portfolio metrics**
  - ETF and price ingestion.
  - Portfolio & transaction modeling.
  - Daily valuation and core metrics.

- **Phase 2 – AI reporting**
  - Monthly digests and explanations of major changes.
  - Event-based reports for significant drawdowns or allocation shifts.

- **Phase 3 – RAG on ETF documents & Q&A**
  - Ingestion of ETF documents and metadata.
  - Semantic search and portfolio-aware Q&A.

- **Phase 4 – Architecture hardening & potential SaaS**
  - Modularization into services, proper observability and resilience.
  - Multi-tenant design and SaaS capabilities if justified.

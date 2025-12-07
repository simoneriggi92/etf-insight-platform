# Build Journal

## 2025-11-27 - Day 1
- Created initial repository structure.
- Wrote first version of README and vision.
- Defined high-level phases for the platform.

## 2025-11-28 Day 2
- Added initial Postgres schema (etf, etf_price_history) in db/init/schema-v0.sql.
- Create docker-compose.yml for Postgres and verified the database is up and reachable.
- Adjusted ETF schema (volume nullable, cleaned up indexes and comments).
- Inserted initial ETF records (VWCE, IWDA, EIMI) into the etf table.
- Created seed-data/etf-prices-sample.csv with sample price history for the initial ETFs.

## 2025-12-01 Day 3
- Created worker project and implemented Program.cs
- Created PriceIngestionWorker that pre-loads CVS data rows, parses lines, loads from db the existing ETFs and upserts ingested data to the etf_price_history table
- Fixed appsettings.Development.json paths definition
- Started ingestion worker and verified data-ingestion and idempotency
- Created .gitignore file with standard patterns for .NET projects (build outputs, IDE files, OS-specified files and other common exclusions)

## 2025-12-02 Day 4
- Created minimal API project to expose ETF data and API status: 
    - /etfs: exposes all etfs saved in the etfs table
    - /etfs/{ticker}/prices?limit: exposes prices history saved in the etf_prices_history, ordered by desc, of the etf ticker passed as parameter 
    - /health: returns the API status 
- Updated schema-v0.sql adding the table's schemas: portfolio and portfolio_transaction. Added some data them manually.

## 2025-12-03 Day 5
- Created .sql script to compute the ETFs positions at a certain date
- Create minimal API endpoint '/portfolios/{id:int}/valuation' which computes the totalValue of an ETF at a certain date

## 2025-12-04 Day 6
- Created launch.json and tasks.json to run in Debug mode EtfInsight.Api project
- Manually tested the valuation API with and without query string parameters and verified the results
- Created valuation worker project and started implementing the logic to:
    - automatically perform valuation for each existing portfolio.
    - insert valuation to new 'portfolio_valuation' table.

## 2025-12-05 Day 5 
- Debugged the EtfInsight.Portfolio.Valuation worker to fix the logic to compute the portfolio valuation
- Changed launch.json and tasks.json to run EtfInsight.Portfolio.Valuation in Debug mode
- Modified the 'portfolio_valuation' table schema in schema-v0.sql

## 2025-12-07 Day 6
- Debuggeds and fixed port EtfInsight.Portfolio.Valuation worker valuation computation.
- Fixed saving portfolio evaluation to 'portfolio_valuation' table.
- Compared results with the minimal API endpoint '/portfolios/{id:int}/valuation' and verified the correctness.
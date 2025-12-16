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

- Debugged and fixed port EtfInsight.Portfolio.Valuation worker valuation computation.
- Fixed saving portfolio evaluation to 'portfolio_valuation' table.
- Compared results with the minimal API endpoint '/portfolios/{id:int}/valuation' and verified the correctness.

## 2025-12-08 Day 7

- Refactored and improved the logic of EtfInsight.Portfolio.Valuation worker
- Verified the correctness of the model computing new valuation based on the insert of new transactions

## 2025-12-09 Day 8

- Implemented new API endpoint '/portfolios/{id:int}/valuation/history' to return portfolio valuations for a specific time range
- Verified numbers and formatted response (response value types)

## 2025-12-10 Day 9

- Improved API endpoint '/portfolios/{id:int}/valuation/history' integrating the logics to compute the following metrics:
  - absoluteChange
  - percentChange
  - netFlow
  - cumulativeNetFlow
  - pnL
  - return

## 2025-12-11 Day 10

- Fixed API endpoint '/portfolios/{id:int}/valuation/history', in particular:
  - how 'cumulativeNetFlow' was computed: the issue was due the computation was not considering all the portfolio transactions from the beginning up to 'toDate' valuation date
  - The transactions loaded was considering the filter 'from-to' timerange, rather than load all portofolio's transaction from the beginning up to valuation date
  - Using portfolio max(valution_date) as fallback whenever 'toDate' parameter is not valued

## 2025-12-14 Day 11

- Implemented '/portfolios/{id:int}/valuation/summary' to return an aggregated view of a portfolio performance within a certain period
- Extracted portfolio's points generation logic to a dedicated method to make it re-usable
- Added dradown metric computation logic
- Created model validation file to show the formulas used for the metrics computation

## 2025-12-15 Day 12

- Added numeric example into 'valuation-model.md' to validate the model and provide a concrete example
- Created 'EtfInsight.Tests' project to create UnitTests for the platform

## 2025-12-16 Day 13

- Moved valuation and summary computation logic to 'ValuationSummaryCalculator' static class to make their related UnitTests more clear
- Created UnitTest based on container and clean PostgreSql image to test valuation computation logic:
  - Implemented 'PostgresFixture' class to handle contrainer lifecycle: initialize and dispose PostgresSql container
  - Implemented 'ValuationHistoryDbTests' unitTest to test valuation logic computation

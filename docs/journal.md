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
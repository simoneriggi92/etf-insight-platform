# EtfInsight.Api.IntegrationTests

Integration tests for the ETF Insight API, testing the interaction between services, repositories, and the PostgreSQL database.

## Overview

These tests verify the complete flow from service layer through repository to database:

- **ValuationService**: Tests portfolio valuation calculations with real database data
- Database setup/cleanup with transaction isolation
- Uses a dedicated test database to avoid polluting development data

## Prerequisites

### Test Database

You need a PostgreSQL test database running. The tests use the following default connection string:

```
Host=localhost;Port=5432;Database=etfinsight_test;Username=etfinsight;Password=devpassword123
```

You can override this by setting the `TEST_DB_CONNECTION` environment variable.

### Create Test Database

```sql
-- Connect to postgres as admin
CREATE DATABASE etfinsight_test;
CREATE USER etfinsight WITH PASSWORD 'devpassword123';
GRANT ALL PRIVILEGES ON DATABASE etfinsight_test TO etfinsight;

-- Connect to etfinsight_test
\c etfinsight_test
GRANT ALL ON SCHEMA public TO etfinsight;
```

### Initialize Schema

Run the schema creation scripts from `src/db/` against the test database:

```bash
# From project root
psql -h localhost -U etfinsight -d etfinsight_test -f src/db/schema.sql
psql -h localhost -U etfinsight -d etfinsight_test -f src/db/02_portfolio_schema.sql
psql -h localhost -U etfinsight -d etfinsight_test -f src/db/03_fx_schema.sql
psql -h localhost -U etfinsight -d etfinsight_test -f src/db/04_add_transaction_currency.sql
```

## Running the Tests

### Via dotnet CLI

```bash
# Run all integration tests
dotnet test tests/EtfInsight.Api.IntegrationTests/

# Run specific test
dotnet test tests/EtfInsight.Api.IntegrationTests/ --filter "FullyQualifiedName~GetHistoryAsync_WithSimplePortfolio"

# Run with detailed output
dotnet test tests/EtfInsight.Api.IntegrationTests/ --logger "console;verbosity=detailed"
```

### Via Visual Studio / Rider

Open Test Explorer and run the tests from there. Make sure your test database is running.

## Test Structure

### DatabaseFixture

Provides:

- Database connection management
- Automatic cleanup between tests (TRUNCATE tables)
- Shared across all tests in the collection

### Test Organization

Tests are organized in the `[Collection("Database")]` to ensure proper fixture sharing and sequential execution when needed.

## What's Tested

### ValuationServiceTests

1. **GetHistoryAsync_WithSimplePortfolio_ReturnsCorrectValuations**
   - Creates portfolio with transactions across multiple ETFs
   - Verifies correct valuation calculation for 3 days
   - Tests holdings quantity and total value

2. **GetHistoryAsync_WithSellTransaction_ReturnsCorrectQuantities**
   - Tests buy and sell transactions
   - Verifies quantity decreases after sell
   - Validates valuation updates correctly

3. **GetHistoryAsync_EmptyPortfolio_ReturnsEmptyHistory**
   - Edge case: portfolio without transactions
   - Ensures no errors with empty data

## Best Practices

- Each test starts with `await _fixture.CleanupAsync()` to ensure clean state
- Use explicit dates (not `DateTime.Now`) for reproducible tests
- Assert on multiple aspects: counts, values, quantities
- Keep tests independent and isolated

## Troubleshooting

### Connection Errors

- Verify PostgreSQL is running: `docker ps` or `brew services list`
- Check connection string matches your setup
- Ensure test database exists and user has permissions

### Schema Errors

- Run schema scripts against test database
- Verify all migrations are applied
- Check column names match expected schema

### Flaky Tests

- Ensure proper cleanup between tests
- Check for timezone issues with dates
- Verify test data doesn't depend on execution order

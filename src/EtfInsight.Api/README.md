## API Endpoints

### Base URL

'http://localhost:5076'

### Available Endpoints

#### Health Check

```httpdumm
GET /health
```

#### Get All Tickers

```http
GET /api/tickers
```

Returns list of tracked ETF Tickers with data range.

#### Get Latest Price

```http
GET /api/prices/latest?ticker={ticker}
```

Example: `api/prices/latest?ticker=SPY`

#### Get Price History

```http
GET /api/prices?ticker={ticker}&from={DATE}&to={DATE}
```

Example: `/api/prices?ticker=QQQ&from=2024-01-01&to=2024-12-31`

Parameters:

- `ticker` (required): ETF ticker
- `from` (optional): Start date (YYYY-MM-DD), default: 30 days ago
- `to` (optional): End date (YYYY-MM-DD), default: today

#### Get Price Statistics

```http
GET /api/prices/stats?ticker={ticker}&from={DATE}&to={DATE}
```

Returns min/max/avg prices, trading days, volume statistics.

### Create portfolio

```http
POST /api/portfolios?name={NAME}&description={DESCRIPTION}&base_currency={CCY}
```

Parameters:

- `name` (required): portfolio name
- `description` (optional): Portfolio description
- `base_currency` (optional): portfolio currency

### Get portfolio

```http
GET /api/portfolios/{id}
```

Returns the portfolio details

### Get all portfolios

```http
GET /api/portfolios
```

Returns the details for all portfolios

### Add transactions to portfolio

```http
POST /api/portfolios/{id}/transactions?ticker={ticker}&transaction_type={TTYPE}&quantity={QTY}&price={PRICE}&currency="{CCY}"&transaction_date={TDATE}&notes="{NOTES}"
```

Parameters:

- `ticker` (required): ETF ticker
- `transaction_type` (required): the transaction type (BUY/SELL)
- `quantity` (required): ETF ticker shares quantity
- `price` (required): ETF ticker shares price per unit
- `currency` (optional): price currency
- `transaction_date` (optional): the date of transaction
- `notes` (optional): the notes of transaction

### Get portfolio valuation

```http
GET /api/portfolios/{id}/valaution?date={DATE}&currency={CCY}
```

Returns the portfolio valuation in a specific currency

### Get portfolio valuation history

```http
GET /api/portfolios/{id}/valaution?from={DATE}&to={DATE}
```

Returns the portfolio valuation history points of a specific time range

### Get portfolio performance

```http
GET /api/portfolios/{id}/performance
```

Returns the portfolio performance details

### Get portfolio summary dashboard

```http
GET /api/portfolios/{id}/dashboard
```

Returns the portfolio summary details

### Interactive Documentation

Visit `http://localhost:5076/swagger` when API is running.

## Running the Project

### Prerequisites

- NET 9 SDK
- Docker & Docker compose
- Python 3.10+

### Start Database

```bash
docker compose up -d
```

### Run Data Ingestion

```bash
cd src/ingestion
source .venv/bin/activate
python backfill-history.py
python load_to_db.py
```

### Start API

```bash
cd src/api/EtfInsight.Api
dotnet run
```

### Access Swagger UI

Open browser: `localhost:5076/swagger`

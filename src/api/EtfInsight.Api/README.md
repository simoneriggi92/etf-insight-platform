## API Endpoints

### Base URL

'http://localhost:5076'

### Available Endpoints

#### Health Check

```http
GET /health
```

#### Get All Symbols

```http
GET /api/symbols
```

Returns list of tracked ETF symbols with data range.

#### Get Latest Price

```http
GET /api/prices/latest?symbol={SYMBOL}
```

Example: `api/prices/latest?symbol=SPY`

#### Get Price History

```http
GET /api/prices?symbol={SYMBOL}&from={DATE}&to={DATE}
```

Example: `/api/prices?symbol=QQQ&from=2024-01-01&to=2024-12-31`

Parameters:

- `symbol` (required): ETF symbol
- `from` (optional): Start date (YYYY-MM-DD), default: 30 days ago
- `to` (optional): End date (YYYY-MM-DD), default: today

#### Get Price Statistics

```http
GET /api/prices/stats?symbol={SYMBOL}&from={DATE}&to={DATE}
```

Returns min/max/avg prices, trading days, volume statistics.

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

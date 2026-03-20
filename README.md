# 📈 ETFInsight: AI-Powered Investment Portfolio Manager

> **A modern financial platform combining rigorous performance analytics (TWRR), local AI (RAG), and Just-in-Time price ingestion to provide actionable investment insights.**

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet)
![Vue.js](https://img.shields.io/badge/Vue.js-3.0-4FC08D?style=flat&logo=vuedotjs)
![Tailwind](https://img.shields.io/badge/Tailwind_CSS-38B2AC?style=flat&logo=tailwind-css)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat&logo=postgresql)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat&logo=docker)
![Apache Airflow](https://img.shields.io/badge/Apache%20Airflow-017CEE?style=flat&logo=Apache%20Airflow&logoColor=white)
![AI](https://img.shields.io/badge/AI-Ollama%20%2B%20RAG-orange?style=flat)
![Status](https://img.shields.io/badge/Status-V2.0%20Active-brightgreen)

---

## 💡 Overview

**ETFInsight** is not just a portfolio tracker. It is a distributed, event-driven system designed to bridge the gap between **Quantitative Finance** and **Semantic AI**.

Most portfolio trackers show you *how much* you have. ETFInsight aims to tell you *why* your portfolio is moving, using a custom-built analytics engine and a local LLM to answer questions like:

- *"Why is my tech exposure risky right now?"*
- *"Find me defensive ETFs similar to this one"*

V2.0 introduces:

- **guest-session multi-tenancy**
- **Just-in-Time (JIT) ticker ingestion**
- **CSV bulk import**
- **Airflow-orchestrated data pipelines**

This allows a user to create a portfolio and add transactions for almost any ticker without pre-loading thousands of symbols in advance.

---

## 🏗️ Architecture

The solution follows **Clean Architecture** and **Domain-Driven Design (DDD)**, runs inside an isolated Docker network, uses **Nginx** as reverse proxy, **Hangfire** for background jobs, and **Apache Airflow** for data engineering workflows.

### V2.0 — JIT Ingestion Flow

```mermaid
sequenceDiagram
    actor User
    participant Vue as Vue 3 SPA
    participant API as .NET 9 API
    participant DB as PostgreSQL
    participant AW as Airflow REST API
    participant YF as yfinance

    User->>Vue: Add transaction for unknown ticker
    Vue->>API: POST /api/portfolios/{id}/transactions
    API->>DB: Upsert etf_metadata(status='pending')
    API->>AW: Trigger etf_backfill_jit DAG
    API->>DB: Mark ticker as ingesting
    API-->>Vue: 202 Accepted

    Note over Vue: Global ingestion badge appears

    AW->>YF: Fetch historical OHLCV
    YF-->>AW: Price rows
    AW->>DB: UPSERT etf_prices
    AW->>API: POST /api/ingestion/callback
    API->>DB: Mark ticker as ready

    Vue->>API: Poll /api/ingestion/{ticker}/status
    API-->>Vue: ready
    Vue->>API: Reload analytics
```

### System Architecture

```mermaid
graph TD
    Client[Browser / User] -->|HTTP :3000| Nginx[Nginx Reverse Proxy]

    subgraph "Docker Network: infra_etf-network"
        Nginx -->|/| Vue[Vue.js 3 SPA]
        Nginx -->|/api/*| API[.NET 9 Web API]

        API --> Engine[Portfolio Analytics + TWRR]
        API --> RAG[AI & Semantic Search]
        API --> JIT[JIT Ingestion Service]
        API --> Hangfire[Background Workers]

        Engine --> DB[(PostgreSQL + pgvector)]
        Hangfire --> DB
        RAG --> DB
        RAG --> Ollama[Ollama on Host]
        JIT --> AirflowWS[Airflow Webserver]

        subgraph "Data Engineering (Airflow)"
            AirflowWS
            Scheduler[Airflow Scheduler] --> Executor[LocalExecutor]
            Executor -->|etf_daily_prices| YF[yfinance]
            Executor -->|etf_backfill_prices| YF
            Executor -->|etf_backfill_jit| YF
            Executor -->|POST callback| API
            YF -->|UPSERT prices| DB
        end
    end
```

---

## 🧩 Key Components

### Frontend SPA (Vue 3 + TypeScript + Pinia)

- Responsive dashboard styled with Tailwind CSS and shadcn-vue
- Guest session persisted in `localStorage` and sent via `X-Guest-Id`
- Global ingestion spinner while any ticker is still loading
- CSV import UI with preview and per-ticker ingestion statuses
- AI advisor panel for local RAG queries

### Core API (.NET 9)

| Project                     | Responsibility                                                     |
| --------------------------- | ------------------------------------------------------------------ |
| `EtfInsight.Core`           | Entities, DTOs, domain interfaces, analytics services              |
| `EtfInsight.Infrastructure` | Dapper repositories, Airflow integration, CSV import, Ollama APIs  |
| `EtfInsight.Api`            | Controllers, middleware, DI wiring, runtime orchestration          |
| `EtfInsight.DataQuality`    | Anomaly rules, scanner, settings, persistence contracts            |

### Performance Engine

- Time-Weighted Rate of Return (TWRR)
- Daily valuation history
- PnL and simple return
- Peak and drawdown analytics

### JIT Ingestion Pipeline

When a user submits a transaction for an unknown ticker, the API:

1. creates or updates a placeholder row in `etf_metadata`
2. triggers `etf_backfill_jit` through the Airflow REST API
3. saves the transaction immediately
4. returns `202 Accepted`
5. waits for Airflow to fetch history and POST a signed callback
6. exposes status through `/api/ingestion/{ticker}/status` until the frontend refreshes analytics

### Multi-Tenancy (Guest Sessions)

- No login required
- Each browser gets a UUID guest id
- `GuestSessionMiddleware` resolves and propagates that id
- `portfolios.user_id` and RLS provide tenant separation for portfolio reads

### CSV Bulk Import

- `multipart/form-data` upload
- parsed with `CsvHelper`
- partial-row validation: bad rows are returned, not fatal
- distinct tickers are pushed through the same JIT flow as manual entry

### AI & Vector Search

- Embeddings generated through Ollama
- Stored in Postgres with pgvector
- Similarity search over `etf_documents`
- Answer generation through local Ollama chat/generate APIs

### Data Quality & Event-Driven Workers

- Hangfire-backed scan scheduling
- negative-price rule
- flash-crash rule
- anomaly persistence in `data_anomalies`

### Data Engineering (Apache Airflow)

| DAG                   | Schedule                | Purpose                                     |
| --------------------- | ----------------------- | ------------------------------------------- |
| `etf_daily_prices`    | Daily (EOD)             | Incremental update for active tickers       |
| `etf_backfill_prices` | Manual                  | Historical backfill for a date range        |
| `etf_backfill_jit`    | On-demand               | JIT backfill for a single new ticker        |
| `data_quality_scan`   | Triggered/manual        | Enqueue anomaly scan in the API             |

---

## 📸 Screenshots

|            Dashboard            |          Portfolio Management           |
| :-----------------------------: | :-------------------------------------: |
|  ![Dashboard](./docs/images/1.png)   | ![Portfolio Management](./docs/images/2.png) |
| **Transactions & Performance** |          **AI Advisor (RAG)** |
| ![Transactions](./docs/images/3.png) |      ![AI Advisor](./docs/images/4.png)      |

### Data Quality Dashboard

![Data Quality](./docs/images/5.png)



|            Portfolio creation            |          Transaction Management           |
| :-----------------------------: | :-------------------------------------: |
|  ![Portfolio creation](./docs/images/2.1.png)   | ![Import transactions](./docs/images/2.2.png) |
| **Transactions & Performance** |          **AI Advisor (RAG)** |
| ![Add Transactions](./docs/images/2.3.png) |          

---

## 🗺️ Roadmap & Progress

### ✅ Phase 1–3: Foundation, Math & AI

- [x] Dockerized local environment
- [x] TWRR implementation and cash-flow-aware analytics
- [x] Ollama + pgvector integration
- [x] Local RAG pipeline

### ✅ Phase 4–6: Trust, Scale & UI

- [x] Audit table for ETF price changes
- [x] Rule-based anomaly detection
- [x] Hangfire background jobs
- [x] Vue 3 + TypeScript SPA
- [x] Dockerized frontend + API deployment

### ✅ Phase 7: Data Engineering

- [x] Replaced legacy scheduled scripts with Airflow
- [x] Daily and backfill DAGs with idempotent upserts

### ✅ Phase 8: JIT Ingestion & Guest Sessions

- [x] `user_id` guest-session tenancy model
- [x] `etf_ingestion_status` lifecycle in `etf_metadata`
- [x] API-triggered `etf_backfill_jit` DAG
- [x] Signed ingestion callback endpoint
- [x] Frontend polling and auto-refresh
- [x] CSV import integrated with JIT flow

### 🔮 Next Steps

- [ ] Airflow pools and rate-limiting for larger ticker volumes
- [ ] Automated PDF factsheet/KIID ingestion and embedding
- [ ] JIT smoke test covering create portfolio → add unknown ticker → poll until ready
- [ ] Stronger tenant isolation across all data paths
- [ ] Full multi-currency valuation support

---

## 🚀 Getting Started

### Prerequisites

- **Docker Desktop**
- **Ollama** running on the host machine

Pull the required models:

```bash
ollama pull nomic-embed-text
ollama pull llama3.2
```

### Installation

```bash
git clone https://github.com/simoneriggi92/ETFInsight.git
cd ETFInsight/infra
docker compose up --build -d
```

---

## 🔧 Configuration Notes

### Ollama

The API container expects Ollama to be reachable at:

```text
http://host.docker.internal:11434
```

That is already wired in `infra/docker-compose.yml`.

### Airflow Callback URL

There are two common development modes:

#### 1. Full Docker mode

API and Airflow both run inside Docker. In this mode, Airflow should callback:

```text
http://etf-api:8080/api/ingestion/callback
```

#### 2. API on host, Airflow in Docker

If you run the API locally with `dotnet run`, Airflow cannot call `etf-api:8080`.  
In that case, set:

```text
DOTNET_API_CALLBACK_URL=http://host.docker.internal:5001/api/ingestion/callback
```

This is the most important setup detail for local JIT ingestion debugging.

### Airflow/Auth variables currently used by the stack

| Variable                                  | Service   | Description                                        |
| ----------------------------------------- | --------- | -------------------------------------------------- |
| `Airflow__BaseUrl`                        | `etf_api` | Airflow webserver URL used by the API              |
| `Airflow__Username` / `Airflow__Password` | `etf_api` | Airflow Basic Auth credentials                     |
| `Airflow__CallbackSecret`                 | `etf_api` | Shared secret expected by `/api/ingestion/callback`|
| `DOTNET_API_CALLBACK_URL`                 | Airflow   | Callback target for `etf_backfill_jit`             |
| `AIRFLOW_CALLBACK_SECRET`                 | Airflow   | Shared secret sent by the DAG callback             |

---

## 🌐 Access Points

| Service            | URL                                   |
| ------------------ | ------------------------------------- |
| Web App            | http://localhost:3000                 |
| Airflow Dashboard  | http://localhost:8090                 |
| API Swagger UI     | http://localhost:5001                 |
| API Health         | http://localhost:5001/health          |
| Hangfire Dashboard | http://localhost:5001/hangfire        |

Note: Swagger is exposed directly by the API container in development mode. It is not the same as the frontend route space under port `3000`.

---

## 🧪 CSV Import

A sample CSV is available at [`docs/jit ingestion/csv_import/sample_transactions.csv`](./jit%20ingestion/csv_import/sample_transactions.csv).

Expected columns:

```csv
ticker,transaction_date,type,units,price_per_unit,fees
VWCE.DE,2024-01-15,BUY,10,98.42,3.95
```

---

## 🛠️ Troubleshooting

### `etf_api` starts but JIT ingestion never completes

Check:

- `etf-postgres` is healthy
- `etf-airflow-webserver` is reachable
- `etf-airflow-scheduler` is running
- `DOTNET_API_CALLBACK_URL` matches your dev mode
- `AIRFLOW_CALLBACK_SECRET` matches `Airflow__CallbackSecret`

Useful commands:

```bash
docker compose ps
docker logs etf-api
docker logs etf-postgres
docker logs etf-airflow-webserver
docker logs etf-airflow-scheduler
```

### API works but the container shows as unhealthy

If the API responds but Docker still marks the container unhealthy, inspect the healthcheck command in `infra/docker-compose.yml` and the runtime image tooling. A mismatched healthcheck can make the container appear unhealthy even when the app process is alive.

---

## 📄 License

MIT

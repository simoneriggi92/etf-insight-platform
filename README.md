
# 📈 ETFInsight: AI-Powered Investment Portfolio Manager

> **A modern financial platform combining rigorous performance analytics (TWRR) with Generative AI (RAG) to provide actionable investment insights.**

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat&logo=dotnet)
![Vue.js](https://img.shields.io/badge/Vue.js-3.0-4FC08D?style=flat&logo=vuedotjs)
![Tailwind](https://img.shields.io/badge/Tailwind_CSS-38B2AC?style=flat&logo=tailwind-css)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat&logo=postgresql)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat&logo=docker)
![AI](https://img.shields.io/badge/AI-Ollama%20%2B%20RAG-orange?style=flat)
![Status](https://img.shields.io/badge/Status-V1.0%20Release-brightgreen)

---

## 💡 Overview

**ETFInsight** is not just a portfolio tracker. It is a distributed, event-driven system designed to bridge the gap between **Quantitative Finance** and **Semantic AI**.

Most portfolio trackers show you *how much* you have. ETFInsight tells you *why* your portfolio is moving, using a custom-built Financial Engine and a local LLM (Large Language Model) to answer questions like:

- *"Why is my tech exposure risky right now?"*
- *"Find me defensive ETFs similar to this one"*

---

## 🏗️ Architecture

The solution follows **Clean Architecture**, **Domain-Driven Design (DDD)**, and operates within an isolated Docker network using an **API-Gateway / Reverse Proxy** pattern.

```mermaid
graph TD
    Client[Browser / User] -->|HTTP :3000| Nginx[Nginx Reverse Proxy]
    
    subgraph "Docker Network"
        Nginx -->|/| Vue[Vue.js 3 SPA]
        Nginx -->|/api/*| API[.NET 9 Web API]
        
        API --> Engine[Performance Engine TWRR]
        API --> RAG[AI & RAG Service]
        API --> Hangfire[Background Workers]
        
        Engine & Hangfire --> DB[(PostgreSQL + pgvector)]
        RAG --> DB
        RAG --> Ollama[Ollama Local LLM]
    end
    
    Scraper[Python Ingestion] -->|INSERT| DB
    Scraper -->|Webhook Trigger| API

---

## 🧩 Key Components
- Frontend SPA (Vue 3 + TypeScript)
- Responsive, data-rich dashboard styled with Tailwind CSS and shadcn-vue. Served blazingly fast via Nginx.

### Core API (.NET 9)
- Manages portfolios, transactions, and orchestrates the AI workflow. Acts as the brain of the operation.

### Performance Engine
- Implements Time-Weighted Rate of Return (TWRR) to calculate accurate performance regardless of cash flows (deposits/withdrawals). Provides analytics like PnL, drawdowns, peaks, and annualized return.

### AI & Vector Search
- Uses pgvector for semantic search on ETF descriptions and Ollama (Llama 3) for Retrieval Augmented Generation (RAG).

### Data Quality & Event-Driven Workers
- Utilizes Hangfire backed by PostgreSQL to run resilient, asynchronous background jobs (anomaly detection, flash-crash protection).

### Data Ingestion (Python)
- Autonomous dockerized scraper to fetch EOD (End-of-Day) market data.

---

## 🗺️ Roadmap & Progress

The project follows a strict **6-Month Architectural Roadmap**.

### ✅ Phase 1: Foundation (Month 1)
- [x] Dockerized environment (Python Scraper + Postgres + .NET API).
- [x] Database schema design (financial strict types).
- [x] Core domain entities (Portfolio, Transactions).

### ✅ Phase 2: The Math Engine (Month 2)
- [x] Implementation of TWRR (Time-Weighted Rate of Return).
- [x] Handling of complex cash flows (Deposits, Withdrawals, Fees).
- [x] Financial dashboard (PnL, Drawdown, Annualized Return).
- [x] Unit testing validation against manual calculations.

### ✅ Phase 3: The AI Brain (Month 3)
- [x] Integration with Ollama (Local LLM).
- [x] pgvector setup for embedding storage (768 dimensions).
- [x] Semantic search engine ("Find ETFs about AI").
- [x] RAG pipeline: chat with your financial data.

### 🚧 Phase 4: Data Quality & Trust (Month 4) — In Progress
- [x] Database auditing (time-travel queries).
- [x] Anomaly detection (flash-crash protection).
- [x] Data validation logic (Specification Pattern).

### ⏳ Phase 5: Event-Driven Architecture (Month 5)
- [x] Background jobs (Hangfire/Quartz).
- [x] Asynchronous ingestion pipeline.

### ⏳ Phase 6: Scale & UI (Month 6)
- [ ] Frontend dashboard.
- [ ] CI/CD pipelines.
- [ ] Final architectural refactoring.

---

## 🚀 Getting Started

### Prerequisites
- **Docker Desktop** (Required)
- **Ollama** installed on host machine (for AI features)

Pull required models:
```bash
ollama pull nomic-embed-text
ollama pull llama3
```

### Installation

1) Clone the repo
```bash
git clone https://github.com/your-username/ETFInsight.git
cd ETFInsight
```

2) Configure environment  
Ensure your `.env` or `appsettings.json` points to the correct Docker host for Ollama (usually `host.docker.internal:11434`).

3) Run with Docker Compose
```bash
docker-compose up --build
```

4) Access the system
- **Swagger API**: `http://localhost:5000/swagger` (or port defined in `docker-compose`)
- **Database**: `localhost:5432` (User/Pass in compose file)

---

## 🧪 Testing the AI

Once running, you can seed the vector database and chat with it:

- **Seed embeddings**: `POST /api/search/seed`
- **Ask a question**: `POST /api/chat`

Example payload:
```json
{
  "question": "Which ETFs are best for exposure to US Tech?"
}
```

---

## 📄 License
MIT

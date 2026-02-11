# 📈 ETFInsight: AI-Powered Investment Portfolio Manager

> **A modern financial platform combining rigorous performance analytics (TWRR) with Generative AI (RAG) to provide actionable investment insights.**

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat&logo=postgresql)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat&logo=docker)
![AI](https://img.shields.io/badge/AI-Ollama%20%2B%20RAG-orange?style=flat)
![Status](https://img.shields.io/badge/Status-Phase%204%3A%20Data%20Quality-yellow)

## 💡 Overview

**ETFInsight** is not just a portfolio tracker. It is a distributed system designed to bridge the gap between **Quantitative Finance** and **Semantic AI**.

Most portfolio trackers show you *how much* you have. ETFInsight tells you *why* your portfolio is moving, using a custom-built Financial Engine and a local LLM (Large Language Model) to answer questions like *"Why is my tech exposure risky right now?"* or *"Find me defensive ETFs similar to this one"*.

## 🏗️ Architecture

The solution follows **Clean Architecture** and **Domain-Driven Design (DDD)** principles.

```mermaid
graph TD
    User[User / Client] --> API[.NET 8 Web API]
    
    subgraph "Core Domain"
        API --> Engine[Performance Engine (TWRR)]
        API --> RAG[RAG & Chat Service]
    end
    
    subgraph "Data & Vector Layer"
        Engine --> DB[(PostgreSQL)]
        RAG --> VectorDB[(pgvector)]
    end
    
    subgraph "External World"
        Scraper[Python Ingestion] --> DB
        RAG --> Ollama[Ollama (Local LLM)]
    end
Key Components
Core API (.NET 8): Manages Portfolios, Transactions, and coordinates the AI workflow.

Performance Engine: Implements Time-Weighted Rate of Return (TWRR) algorithm to calculate accurate performance regardless of cash flows (Deposits/Withdrawals). Handles math for Drawdowns, Peaks, and PnL.

AI & Vector Search: Uses pgvector for semantic search on ETF descriptions and Ollama (Llama 3) for Retrieval Augmented Generation (RAG).

Data Ingestion (Python): Autonomous dockerized scraper to fetch EOD (End-of-Day) market data.

🗺️ Roadmap & Progress
The project follows a strict 6-Month Architectural Roadmap.

[x] Phase 1: Foundation (Month 1)

[x] Dockerized environment (Python Scraper + Postgres + .NET API).

[x] Database Schema Design (Financial strict types).

[x] Core Domain Entities (Portfolio, Transactions).

[x] Phase 2: The Math Engine (Month 2)

[x] Implementation of TWRR (Time-Weighted Rate of Return).

[x] Handling of complex cash flows (Deposits, Withdrawals, Fees).

[x] Financial Dashboard (PnL, Drawdown, Annualized Return).

[x] Unit Testing validation against manual calculations.

[x] Phase 3: The AI Brain (Month 3)

[x] Integration with Ollama (Local LLM).

[x] pgvector setup for Embedding storage (768 dimensions).

[x] Semantic Search Engine ("Find ETFs about AI").

[x] RAG Pipeline: Chat with your financial data.

[ ] Phase 4: Data Quality & Trust (Month 4) 🚧 In Progress

[ ] Database Auditing (Time-Travel queries).

[ ] Anomaly Detection (Flash Crash protection).

[ ] Data Validation Logic (Specification Pattern).

[ ] Phase 5: Event-Driven Architecture (Month 5)

[ ] Background Jobs (Hangfire/Quartz).

[ ] Asynchronous Ingestion Pipeline.

[ ] Phase 6: Scale & UI (Month 6)

[ ] Frontend Dashboard.

[ ] CI/CD Pipelines.

[ ] Final Architectural Refactoring.

🚀 Getting Started
Prerequisites
Docker Desktop (Required)

Ollama installed on host machine (for AI features)

ollama pull nomic-embed-text

ollama pull llama3

Installation
Clone the repo

Bash
git clone [https://github.com/your-username/ETFInsight.git](https://github.com/your-username/ETFInsight.git)
cd ETFInsight
Configure Environment Ensure your .env or appsettings.json points to the correct Docker host for Ollama (usually host.docker.internal:11434).

Run with Docker Compose

Bash
docker-compose up --build
Access the System

Swagger API: http://localhost:5000/swagger (or port defined in docker-compose)

Database: localhost:5432 (User/Pass in compose file)

🧪 Testing the AI
Once running, you can seed the vector database and chat with it:

Seed Embeddings: POST /api/search/seed

Ask a Question: POST /api/chat

JSON
{
  "question": "Which ETFs are best for exposure to US Tech?"
}
📄 License
MIT

# 📈 ETFInsight: AI-Powered Investment Portfolio Manager

> **A modern financial platform combining rigorous performance analytics (TWRR) with Generative AI (RAG) to provide actionable investment insights.**

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat&logo=postgresql)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat&logo=docker)
![AI](https://img.shields.io/badge/AI-Ollama%20%2B%20RAG-orange?style=flat)
![Status](https://img.shields.io/badge/Status-Phase%204%3A%20Data%20Quality-yellow)

---

## 💡 Overview

**ETFInsight** is not just a portfolio tracker. It is a distributed system designed to bridge the gap between **Quantitative Finance** and **Semantic AI**.

Most portfolio trackers show you *how much* you have. ETFInsight tells you *why* your portfolio is moving, using a custom-built Financial Engine and a local LLM (Large Language Model) to answer questions like:

- *"Why is my tech exposure risky right now?"*
- *"Find me defensive ETFs similar to this one"*

---

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

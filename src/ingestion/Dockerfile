# Multi-stage Dockerfile for ETF Ingestion Service
FROM python:3.11-slim AS base

# Create no-root user
RUN groupadd -r etfuser && useradd -r -g etfuser etfuser

# Set working directory
WORKDIR /app

# Install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application files
COPY ingest_prices_yfinance.py .
COPY load_to_db.py .

# Create data directory for raw files
RUN mkdir -p /app/data/raw && chown -R etfuser:etfuser /app

# Switch to non-root user
USER etfuser

# Set Python to run in unbuffered mode for real-time logs
ENV PYTHONUNBUFFERED=1

# Stage for scraper service
FROM base AS scraper
CMD ["python", "-u", "ingest_prices_yfinance.py"]

# Stage for ingestor service
FROM base AS ingestor
CMD ["python", "-u", "load_to_db.py"]
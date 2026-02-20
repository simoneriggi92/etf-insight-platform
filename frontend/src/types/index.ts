// ── Data Quality ─────────────────────────────────────────────────────────────

export type Severity = 'WARNING' | 'ERROR' 


export interface DataAnomalyResponse{
    success: boolean,
    count: number,
    anomalies: DataAnomaly[]
}

export interface DataAnomaly {
  id: string                  // Guid → string
  ticker: string
  priceDate: string           // DATE → ISO string "YYYY-MM-DD"
  ruleName: string
  severity: Severity
  currentValue: number | null
  expectedRange: string | null
  message: string
  metadata: string | null     // serialized JSON blob
  detectedAt: string          // DateTime → ISO string
  resolved: boolean
}

export interface ScanEnqueuedResponse {
  success: boolean
  message: string
  jobId: string
}

// ── ETF Prices ───────────────────────────────────────────────────────────────

export interface EtfPrice {
  ticker: string
  priceDate: string
  closePrice: number
  volume: number | null
  source: string | null
}

// ── Portfolios ───────────────────────────────────────────────────────────────

export interface Portfolio {
  id: string
  name: string
  createdAt: string
}

export interface PortfolioHolding {
  id: string
  portfolioId: string
  ticker: string
  quantity: number
  purchasePrice: number
  purchaseDate: string
}

// ── Health ───────────────────────────────────────────────────────────────────

export interface HealthStatus {
  status: string
  database: string
  vectorExtension: string
  sampleDocuments: number
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export interface KpiCard {
  label: string
  value: string
  sub?: string
  trend?: 'up' | 'down' | 'neutral'
}
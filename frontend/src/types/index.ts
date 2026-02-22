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

// ...existing code...

// ── Portfolios ────────────────────────────────────────────────────────────────

export type TransactionType = 'BUY' | 'SELL' | 'DEPOSIT' | 'WITHDRAW'

export interface Transaction {
  id: string
  portfolioId: string
  ticker: string
  type: TransactionType
  units: number
  pricePerUnit: number
  fees: number
  transactionDate: string   // DateOnly → "YYYY-MM-DD"
  notes: string | null
}

export interface Portfolio {
  id: string
  name: string
  createdAt: string
  transactions: Transaction[]
}

// ── Portfolio Analytics (GET /api/Portfolios/{id}/analytics/dashboard) ────────

export interface DailyValuationPoint {
  date: string              // DateOnly → "YYYY-MM-DD"
  totalValue: number
  netFlow: number
  cumulativeNetFlow: number
  pnL: number
  return: number
  peak: number
  drawdown: number
  dailyChangePercentage: number
}

export interface PortfolioDashboardDto {
  portfolioId: string
  referenceDate: string
  currentTotalValue: number
  totalInvested: number
  absolutePnL: number
  simpleReturn: number      // decimal e.g. 0.087 = 8.7%
  maxDrawdown: number       // decimal e.g. -0.12 = -12%
  history: DailyValuationPoint[]
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



// ── AI Advisor ────────────────────────────────────────────────────────────────

export interface AiSource {
  ticker: string
  excerpt: string
  similarity: number
}

export interface AiQueryRequest {
  question: string
}

export interface AiQueryResponse {
  question: string
  answer: string
  sources: AiSource[],
  timestamp: string
}

export interface AiMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  sources?: AiSource[]
  timestamp: string
}
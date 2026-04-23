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

export interface PortfolioSummaryDto {
  portfolioId:       string
  twrrYtd:           number   // raw decimal, e.g. 0.0523
  twrrYtdPercentage: string   // formatted string, e.g. "5.23%"
  analysisPeriod:    { from: string; to: string }
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


// ── Import Jobs ───────────────────────────────────────────────────────────────
export type BrokerImportJobStatus =
  | 'queued'
  | 'processing'
  | 'waiting_for_ingestion'
  | 'completed'
  | 'completed_with_errors'
  | 'failed'
  | 'not_found'

export type BrokerImportItemStatus =
  | 'queued'
  | 'parsing'
  | 'parsed'
  | 'duplicate'
  | 'unsupported'
  | 'unresolved_instrument'
  | 'waiting_for_ingestion'
  | 'imported'
  | 'failed'

export interface StartBrokerImportResponse {
  jobId: string
  status: BrokerImportJobStatus
  totalFiles: number
  message: string
}

export interface ImportJobItemResult {
  fileName: string
  status: BrokerImportItemStatus
  isin: string | null
  resolvedTicker: string | null
  errorMessage: string | null
}

export interface ImportJobStatusResponse {
  jobId: string
  status: BrokerImportJobStatus
  totalFiles: number
  processedFiles: number
  importedFiles: number
  duplicateFiles: number
  failedFiles: number
  waitingForIngestionFiles: number
  currentFileName: string | null
  currentMessage: string | null
  errorSummary: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  recentItems: ImportJobItemResult[]
  tickerIngestionStatuses: Record<string, string>
}

// ── Import Archive ────────────────────────────────────────────────────────────

export interface BrokerImportJobSummary {
  jobId: string
  broker: string
  status: BrokerImportJobStatus
  totalFiles: number
  processedFiles: number
  importedFiles: number
  duplicateFiles: number
  failedFiles: number
  waitingForIngestionFiles: number
  errorSummary: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export interface BrokerImportItemDetail {
  fileName: string
  status: BrokerImportItemStatus
  isin: string | null
  instrumentName: string | null
  resolvedTicker: string | null
  transactionType: string | null
  transactionDate: string | null
  settlementDate: string | null
  units: number | null
  pricePerUnit: number | null
  fees: number | null
  grossAmount: number | null
  currency: string | null
  brokerReference: string | null
  brokerSecondaryReference: string | null
  errorMessage: string | null
}

export interface BrokerImportJobDetail {
  jobId: string
  broker: string
  status: BrokerImportJobStatus
  totalFiles: number
  importedFiles: number
  duplicateFiles: number
  failedFiles: number
  errorSummary: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  items: BrokerImportItemDetail[]
}
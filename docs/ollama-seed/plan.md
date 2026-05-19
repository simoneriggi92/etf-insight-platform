# Plan: RAG Phase 2 — PDF Chunking, Ingestion & Chat Service Upgrade

## Objective

Transform the RAG system from a hardcoded seed of 13 descriptions into an automated pipeline that parses downloaded ETF factsheet PDFs into chunked vector embeddings and serves them through a secured, resilient, and cancellation-aware .NET API. The LLM must never compute financial metrics — it receives pre-calculated portfolio data as deterministic context alongside the semantic search results.

## Approach

Two workstreams execute in sequence:

1. **Python (Airflow)**: Extend the existing `etf_knowledge_builder` DAG with new tasks that parse PDFs, chunk text with sliding windows, generate embeddings via Ollama, and POST them to the .NET API.
2. **.NET (API)**: Create a secured ingestion endpoint, refactor the search/chat stack for multi-chunk support, propagate `CancellationToken` end-to-end, return typed DTOs, and inject deterministic portfolio context into the RAG prompt.

**Why this over alternatives:**
- Embedding generation stays in Python (Airflow already has Ollama network access and retry infrastructure). Avoids duplicating HTTP+retry logic in .NET for a batch operation.
- The .NET API receives pre-computed embeddings — it never calls Ollama during ingestion, keeping the API fast and Ollama load predictable.
- Delete-and-replace per ticker inside a transaction avoids orphan chunks and partial updates without complex diffing.

## Out of Scope

- Multi-language support for seed descriptions or prompts.
- Migrating from Ollama `/api/embeddings` to `/api/embed` (tracked separately).
- Streaming chat responses.
- Frontend changes.
- EF Core migration tooling (the vector table uses Dapper/raw SQL by design).

## Files to Modify

| File | Change |
|---|---|
| `airflow/requirements.txt` | Add `pdfplumber`, `httpx` |
| `airflow/dags/etf_knowledge_builder.py` | Add `parse_and_chunk` and `ingest_chunks` tasks |
| `airflow/plugins/hooks/etf_db_hook.py` | Add `get_downloaded_factsheets()` helper |
| `src/db/04_etf_documents_schema.sql` | Drop `UNIQUE` on `ticker`, add `chunk_index`, `source` columns |
| `src/EtfInsight.Core/Interfaces/ISemanticSearchRepository.cs` | Add `CancellationToken`, add `BulkReplaceAsync`, update `SearchAsync` signature |
| `src/EtfInsight.Core/Interfaces/IEmbeddingGenerator.cs` | Add `CancellationToken` parameter |
| `src/EtfInsight.Core/Services/IChatService.cs` | Return `ChatResponseDto` instead of `string`, add `CancellationToken` |
| `src/EtfInsight.Core/DTOs/SearchResult.cs` | Convert to `record` |
| `src/EtfInsight.Core/Configuration/AISettings.cs` | Add `IngestApiKey`, `MinSimilarityThreshold`, `MaxContextChunks` |
| `src/EtfInsight.Infrastructure/Repositories/DapperSemanticSearchRepository.cs` | Implement `BulkReplaceAsync`, add similarity threshold + limit to `SearchAsync`, propagate `CancellationToken` |
| `src/EtfInsight.Infrastructure/Services/OllamaEmbeddingService.cs` | Propagate `CancellationToken` |
| `src/EtfInsight.Infrastructure/Services/OllamaChatService.cs` | Return `ChatResponseDto`, inject `IPortfolioAnalyticsService` + `IHttpContextAccessor`, build portfolio context, fix prompt formatting, propagate `CancellationToken` |
| `src/EtfInsight.Api/Controllers/SemanticSearchController.cs` | Remove `SeedData` endpoint, add `CancellationToken`, add typed response DTOs |
| `src/EtfInsight.Api/Controllers/ChatController.cs` | Remove duplicate embedding call, use `ChatResponseDto`, inject only `IChatService`, add `CancellationToken` |
| `src/EtfInsight.Api/Program.cs` | Register `ApiKeyMiddleware`, register `IHttpContextAccessor`, add `IngestApiKey` config |
| `src/EtfInsight.Api/appsettings.json` | Add `IngestApiKey`, `MinSimilarityThreshold`, `MaxContextChunks` to `AI` section |
| `infra/docker-compose.yml` | Add `AI__IngestApiKey` env var to `etf_api` and `etf-airflow-*` services |

## Files to Create

| File | Responsibility |
|---|---|
| `airflow/include/transforms/factsheet_chunker.py` | PDF parsing + sliding window chunking + Ollama embedding generation |
| `src/EtfInsight.Api/Middleware/ApiKeyMiddleware.cs` | Validates `X-API-Key` header on routes marked with `[ApiKeyRequired]` |
| `src/EtfInsight.Api/Attributes/ApiKeyRequiredAttribute.cs` | Marker attribute for endpoints requiring M2M API key |
| `src/EtfInsight.Api/Controllers/IngestController.cs` | `POST /api/search/ingest` — receives pre-embedded chunks from Airflow |
| `src/EtfInsight.Core/DTOs/ChatResponseDto.cs` | Typed response record for chat answers with sources |
| `src/EtfInsight.Core/DTOs/IngestRequestDto.cs` | Typed request record for chunk ingestion |
| `src/db/11_etf_documents_multi_chunk.sql` | Migration script: drop unique, add columns |

## Implementation

### 1. Schema Migration — Multi-Chunk Support

The current `etf_documents` table has a `UNIQUE` constraint on `ticker`, limiting it to one row per ticker. We need multiple chunks per ticker.

**Migration script** (`src/db/11_etf_documents_multi_chunk.sql`):

```sql
-- Drop the unique constraint on ticker to allow multiple chunks per ticker
ALTER TABLE etf_documents DROP CONSTRAINT IF EXISTS etf_documents_ticker_key;

-- Add chunk ordering and source tracking
ALTER TABLE etf_documents ADD COLUMN IF NOT EXISTS chunk_index INT NOT NULL DEFAULT 0;
ALTER TABLE etf_documents ADD COLUMN IF NOT EXISTS source VARCHAR(50) NOT NULL DEFAULT 'manual_seed';

-- Create a unique constraint on (ticker, chunk_index) to prevent duplicate chunks
ALTER TABLE etf_documents ADD CONSTRAINT uq_etf_documents_ticker_chunk 
    UNIQUE (ticker, chunk_index);
```

### 2. Python — PDF Parsing & Chunking Transform

New file: `airflow/include/transforms/factsheet_chunker.py`

```python
from __future__ import annotations

import pdfplumber
import httpx
import json
import math

CHUNK_SIZE_CHARS = 2000        # ~500 tokens ≈ 2000 chars for English/Italian
OVERLAP_FRACTION = 0.12        # 12% overlap
OLLAMA_URL = "http://host.docker.internal:11434"
EMBEDDING_MODEL = "nomic-embed-text"


def extract_text_from_pdf(pdf_path: str) -> str:
    """Extract full text from a PDF using pdfplumber."""
    with pdfplumber.open(pdf_path) as pdf:
        pages = [page.extract_text() or "" for page in pdf.pages]
    return "\n".join(pages).strip()


def sliding_window_chunk(text: str, chunk_size: int = CHUNK_SIZE_CHARS, overlap_fraction: float = OVERLAP_FRACTION) -> list[str]:
    """Split text into overlapping chunks using a sliding window."""
    if not text:
        return []
    overlap = int(chunk_size * overlap_fraction)
    step = chunk_size - overlap
    chunks = []
    for start in range(0, len(text), step):
        chunk = text[start:start + chunk_size].strip()
        if chunk:
            chunks.append(chunk)
        if start + chunk_size >= len(text):
            break
    return chunks


def generate_embedding(text: str, client: httpx.Client) -> list[float]:
    """Generate a 768-dim embedding via Ollama /api/embeddings."""
    resp = client.post(
        f"{OLLAMA_URL}/api/embeddings",
        json={"model": EMBEDDING_MODEL, "prompt": text},
        timeout=60.0,
    )
    resp.raise_for_status()
    return resp.json()["embedding"]


def process_factsheet(
    ticker: str,
    pdf_path: str,
    client: httpx.Client,
) -> list[dict]:
    """Parse a PDF, chunk it, embed each chunk. Returns list of chunk dicts."""
    text = extract_text_from_pdf(pdf_path)
    if not text:
        raise ValueError(f"No text extracted from {pdf_path}")

    chunks = sliding_window_chunk(text)
    results = []
    for idx, chunk_text in enumerate(chunks):
        embedding = generate_embedding(chunk_text, client)
        results.append({
            "content": chunk_text,
            "embedding": embedding,
            "chunkIndex": idx,
            "metadata": {
                "source": "factsheet",
                "pdfPath": pdf_path,
                "chunkIndex": idx,
                "totalChunks": len(chunks),
            },
        })
    return results
```

### 3. Python — Extend `etf_knowledge_builder` DAG

Add two new tasks after `retrieve_factsheets`:

```python
# In etf_knowledge_builder.py — new tasks

from include.transforms.factsheet_chunker import process_factsheet
import httpx
import json
import os

DOTNET_API_URL = os.environ.get("DOTNET_API_URL", "http://etf-api:8080")
INGEST_API_KEY = os.environ.get("INGEST_API_KEY", "")


def _parse_and_embed(**ctx) -> None:
    """Parse downloaded PDFs, chunk, embed, and POST to .NET ingest endpoint."""
    hook = ETFDatabaseHook()
    factsheets = hook.get_downloaded_factsheets()

    with httpx.Client(timeout=120.0) as ollama_client:
        with httpx.Client(
            base_url=DOTNET_API_URL,
            headers={"X-API-Key": INGEST_API_KEY},
            timeout=60.0,
        ) as api_client:
            for fs in factsheets:
                ticker = fs["ticker"]
                pdf_path = fs["local_path"]
                try:
                    chunks = process_factsheet(ticker, pdf_path, ollama_client)
                    payload = {"ticker": ticker, "chunks": chunks}
                    resp = api_client.post("/api/search/ingest", json=payload)
                    resp.raise_for_status()
                    print(f"[ingest] OK {ticker}: {len(chunks)} chunks")
                except Exception as e:
                    print(f"[ingest] FAIL {ticker}: {e}")
```

New helper in `etf_db_hook.py`:

```python
def get_downloaded_factsheets(self) -> list[dict]:
    rows = self.get_records("""
        SELECT ticker, local_path
        FROM etf_factsheet_status
        WHERE status = 'downloaded' AND local_path IS NOT NULL
        ORDER BY ticker
    """)
    return [{"ticker": r[0], "local_path": r[1]} for r in rows]
```

DAG wiring:

```python
get_pending_isins >> retrieve_factsheets >> parse_and_embed
```

### 4. .NET — API Key Middleware

**`src/EtfInsight.Api/Attributes/ApiKeyRequiredAttribute.cs`**:

```csharp
namespace EtfInsight.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyRequiredAttribute : Attribute;
```

**`src/EtfInsight.Api/Middleware/ApiKeyMiddleware.cs`**:

```csharp
using EtfInsight.Api.Attributes;
using EtfInsight.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EtfInsight.Api.Middleware;

public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public async Task InvokeAsync(HttpContext ctx, IOptions<AISettings> settings)
    {
        var endpoint = ctx.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ApiKeyRequiredAttribute>() is null)
        {
            await next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
            || string.IsNullOrWhiteSpace(settings.Value.IngestApiKey)
            || !string.Equals(providedKey, settings.Value.IngestApiKey, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
            return;
        }

        await next(ctx);
    }
}
```

### 5. .NET — DTOs

**`src/EtfInsight.Core/DTOs/IngestRequestDto.cs`**:

```csharp
namespace EtfInsight.Core.DTOs;

public sealed record IngestRequestDto
{
    public required string Ticker { get; init; }
    public required IReadOnlyList<IngestChunkDto> Chunks { get; init; }
}

public sealed record IngestChunkDto
{
    public required string Content { get; init; }
    public required float[] Embedding { get; init; }
    public required int ChunkIndex { get; init; }
    public required Dictionary<string, object> Metadata { get; init; }
}
```

**`src/EtfInsight.Core/DTOs/ChatResponseDto.cs`**:

```csharp
namespace EtfInsight.Core.DTOs;

public sealed record ChatResponseDto
{
    public required string Answer { get; init; }
    public required IReadOnlyList<SearchResultDto> Sources { get; init; }
}

public sealed record SearchResultDto
{
    public required string Ticker { get; init; }
    public required string Content { get; init; }
    public required double Similarity { get; init; }
}
```

**`src/EtfInsight.Core/DTOs/SearchResult.cs`** — convert to record:

```csharp
namespace EtfInsight.Core.DTOs;

public sealed record SearchResult
{
    public required string Ticker { get; init; }
    public required string Content { get; init; }
    public required double Similarity { get; init; }
}
```

### 6. .NET — AISettings Additions

```csharp
public class AISettings
{
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ChatModel { get; set; } = "llama3.2";
    public int EmbeddingDimensions { get; set; } = 768;
    public string IngestApiKey { get; set; } = string.Empty;
    public double MinSimilarityThreshold { get; set; } = 0.65;
    public int MaxContextChunks { get; set; } = 7;
}
```

`appsettings.json` additions:

```json
"AI": {
    "IngestApiKey": "",
    "MinSimilarityThreshold": 0.65,
    "MaxContextChunks": 7
}
```

### 7. .NET — Interface Changes (CancellationToken)

**`IEmbeddingGenerator`**:

```csharp
public interface IEmbeddingGenerator
{
    Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken ct = default);
}
```

**`ISemanticSearchRepository`**:

```csharp
public interface ISemanticSearchRepository
{
    Task SaveEmbeddingAsync(string ticker, string content, float[] embedding, CancellationToken ct = default);
    Task BulkReplaceAsync(string ticker, IReadOnlyList<IngestChunkDto> chunks, CancellationToken ct = default);
    Task<IEnumerable<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5, double minSimilarity = 0.65, CancellationToken ct = default);
}
```

**`IChatService`**:

```csharp
public interface IChatService
{
    Task<ChatResponseDto> AskAiAsync(string question, Guid userId, CancellationToken ct = default);
}
```

### 8. .NET — Repository: BulkReplaceAsync & Search Threshold

In `DapperSemanticSearchRepository`:

**`BulkReplaceAsync`** — delete-and-replace inside a transaction:

```csharp
public async Task BulkReplaceAsync(string ticker, IReadOnlyList<IngestChunkDto> chunks, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(ticker);
    ArgumentNullException.ThrowIfNull(chunks);

    if (_connection.State != ConnectionState.Open)
        _connection.Open();

    using var transaction = _connection.BeginTransaction();
    try
    {
        await _connection.ExecuteAsync(
            "DELETE FROM etf_documents WHERE ticker = @Ticker",
            new { Ticker = ticker },
            transaction);

        foreach (var chunk in chunks)
        {
            var embeddingString = $"[{string.Join(",", chunk.Embedding.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]";
            var metadataJson = JsonSerializer.Serialize(chunk.Metadata);

            await _connection.ExecuteAsync(@"
                INSERT INTO etf_documents (ticker, content, embedding, metadata, is_mandatory, chunk_index, source)
                VALUES (@Ticker, @Content, @Embedding::vector, @Metadata::jsonb, false, @ChunkIndex, @Source)",
                new
                {
                    Ticker = ticker,
                    Content = chunk.Content,
                    Embedding = embeddingString,
                    Metadata = metadataJson,
                    ChunkIndex = chunk.ChunkIndex,
                    Source = chunk.Metadata.GetValueOrDefault("source", "unknown")?.ToString() ?? "unknown"
                },
                transaction);
        }

        transaction.Commit();
        _logger.LogInformation("Replaced {Count} chunks for {Ticker}", chunks.Count, ticker);
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

**`SearchAsync`** — add similarity threshold:

```csharp
public async Task<IEnumerable<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5, double minSimilarity = 0.65, CancellationToken ct = default)
{
    var sql = @"
        SELECT ticker, content, 1 - (embedding <=> @QueryEmbedding::vector) AS similarity
        FROM etf_documents
        WHERE 1 - (embedding <=> @QueryEmbedding::vector) >= @MinSimilarity
        ORDER BY embedding <=> @QueryEmbedding::vector
        LIMIT @Limit";

    var parameters = new
    {
        QueryEmbedding = $"[{string.Join(",", queryEmbedding.Select(f => f.ToString(CultureInfo.InvariantCulture)))}]",
        Limit = limit,
        MinSimilarity = minSimilarity
    };

    var cmd = new CommandDefinition(sql, parameters, cancellationToken: ct);
    return await _connection.QueryAsync<SearchResult>(cmd);
}
```

### 9. .NET — OllamaEmbeddingService CancellationToken

```csharp
public async Task<float[]> GenerateEmbeddingAsync(string input, CancellationToken ct = default)
{
    // ...existing request building...
    var response = await _httpClient.PostAsync("/api/embeddings", content, ct);
    response.EnsureSuccessStatusCode();
    var jsonResponse = await response.Content.ReadAsStringAsync(ct);
    // ...existing deserialization...
}
```

### 10. .NET — OllamaChatService Refactor

Key changes:
- Return `ChatResponseDto` (answer + sources) to eliminate the double-embedding in the controller.
- Accept `Guid userId` to fetch portfolio context.
- Inject `IPortfolioAnalyticsService` and `IOptions<AISettings>` for threshold/limit config.
- Fix prompt formatting with proper newlines.
- Propagate `CancellationToken`.

```csharp
public sealed class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly AISettings _aiSettings;
    private readonly ILogger<OllamaChatService> _logger;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ISemanticSearchRepository _semanticSearchRepository;
    private readonly IPortfolioAnalyticsService _portfolioAnalyticsService;
    private readonly IPortfolioRepository _portfolioRepo;

    // Constructor injects all dependencies

    public async Task<ChatResponseDto> AskAiAsync(string question, Guid userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);

        var questionEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(question, ct);

        var searchResults = await _semanticSearchRepository.SearchAsync(
            questionEmbedding,
            limit: _aiSettings.MaxContextChunks,
            minSimilarity: _aiSettings.MinSimilarityThreshold,
            ct: ct);

        var relevantDocs = searchResults.ToList();

        // Fetch portfolio snapshot for deterministic context
        string? portfolioContext = null;
        if (userId != Guid.Empty)
        {
            portfolioContext = await BuildPortfolioContextAsync(userId, ct);
        }

        var augmentedPrompt = BuildAugmentedPrompt(question, relevantDocs, portfolioContext);

        var answer = await GenerateResponseAsync(augmentedPrompt, ct);

        return new ChatResponseDto
        {
            Answer = answer,
            Sources = relevantDocs.Select(r => new SearchResultDto
            {
                Ticker = r.Ticker,
                Content = r.Content,
                Similarity = r.Similarity
            }).ToList()
        };
    }

    private async Task<string?> BuildPortfolioContextAsync(Guid userId, CancellationToken ct)
    {
        var portfolios = await _portfolioRepo.GetAllPortfoliosWithTransactionsAsync(userId);
        var portfolio = portfolios.FirstOrDefault();
        if (portfolio is null) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oneYearAgo = today.AddYears(-1);
        var dashboard = await _portfolioAnalyticsService.GetPortfolioAnalyticsAsync(
            portfolio.Id, oneYearAgo, today);

        if (dashboard.CurrentTotalValue == 0) return null;

        return $"""
            PORTFOLIO SNAPSHOT (pre-calculated, do NOT recalculate these values):
            - Total Value: €{dashboard.CurrentTotalValue:N2}
            - Total Invested: €{dashboard.TotalInvested:N2}
            - Absolute P&L: €{dashboard.AbsolutePnL:N2}
            - Simple Return: {dashboard.SimpleReturn:P2}
            - Max Drawdown: {dashboard.MaxDrawdown:P2}
            """;
    }

    private string BuildAugmentedPrompt(
        string question,
        List<SearchResult> relevantDocs,
        string? portfolioContext)
    {
        var contextBuilder = new StringBuilder();

        if (portfolioContext is not null)
        {
            contextBuilder.AppendLine(portfolioContext);
            contextBuilder.AppendLine();
        }

        contextBuilder.AppendLine("AVAILABLE ETF CONTEXT:");
        contextBuilder.AppendLine();

        for (int i = 0; i < relevantDocs.Count; i++)
        {
            var doc = relevantDocs[i];
            contextBuilder.AppendLine($"[Document {i + 1}] {doc.Ticker}:");
            contextBuilder.AppendLine(doc.Content);
            contextBuilder.AppendLine($"(Relevance: {doc.Similarity:P1})");
            contextBuilder.AppendLine();
        }

        return $"""
            You are an AI financial assistant expert in ETFs.

            {contextBuilder}

            INSTRUCTIONS:
            - Answer the question using ONLY the provided context.
            - NEVER calculate or estimate financial metrics. Use only the pre-calculated values from the PORTFOLIO SNAPSHOT.
            - If the answer cannot be generated from the available information, reply: "I don't have enough information to answer this question."
            - Be accurate and concise in your answers.
            - Mention the source ETF(s) in your answer if applicable and relevant.
            - Answer in the same language as the USER QUESTION.

            USER QUESTION: {question}

            ANSWER:
            """;
    }
}
```

### 11. .NET — IngestController

```csharp
using EtfInsight.Api.Attributes;
using EtfInsight.Core.DTOs;
using EtfInsight.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EtfInsight.Api.Controllers;

[ApiController]
[Route("api/search")]
[Produces("application/json")]
public sealed class IngestController : ControllerBase
{
    private readonly ISemanticSearchRepository _repository;
    private readonly ILogger<IngestController> _logger;

    public IngestController(
        ISemanticSearchRepository repository,
        ILogger<IngestController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [ApiKeyRequired]
    public async Task<IActionResult> IngestChunksAsync(
        [FromBody] IngestRequestDto request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Ticker))
            return BadRequest(new { error = "Ticker is required" });

        if (request.Chunks.Count == 0)
            return BadRequest(new { error = "At least one chunk is required" });

        _logger.LogInformation("Ingesting {Count} chunks for {Ticker}", request.Chunks.Count, request.Ticker);

        await _repository.BulkReplaceAsync(request.Ticker, request.Chunks, ct);

        return Ok(new { ticker = request.Ticker, chunksIngested = request.Chunks.Count });
    }
}
```

### 12. .NET — ChatController Simplification

Remove `IEmbeddingGenerator` and `ISemanticSearchRepository` injections. Use only `IChatService`:

```csharp
[ApiController]
[Route("api/chat")]
[Produces("application/json")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly IChatService _chatService;

    public ChatController(ILogger<ChatController> logger, IChatService chatService)
    {
        _logger = logger;
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<IActionResult> AskAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question cannot be empty" });

        var userId = HttpContext.GetGuestId();
        var response = await _chatService.AskAiAsync(request.Question, userId, ct);

        return Ok(new
        {
            question = request.Question,
            answer = response.Answer,
            sources = response.Sources.Select(s => new
            {
                ticker = s.Ticker,
                similarity = Math.Round(s.Similarity, 3),
                excerpt = s.Content.Length > 100 ? s.Content[..100] + "..." : s.Content
            }),
            timestamp = DateTime.UtcNow
        });
    }

    // ...existing GetSuggestions...
}
```

### 13. .NET — SemanticSearchController Cleanup

Remove the `SeedData` endpoint entirely. Add `CancellationToken` to `Query`:

```csharp
[HttpPost("query")]
public async Task<IActionResult> QueryAsync([FromBody] SearchRequest request, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(request.Query))
        return BadRequest(new { error = "Query text cannot be empty" });

    var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(request.Query, ct);
    var results = await _semanticSearchRepository.SearchAsync(
        queryEmbedding, request.Limit ?? 5, ct: ct);

    return Ok(new { success = true, query = request.Query, results });
}
```

### 14. .NET — Program.cs Registration

```csharp
// Add before app.UseMiddleware<GuestSessionMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();

// Add to services
builder.Services.AddHttpContextAccessor();
```

## Schema / Type Changes

### `etf_documents` — Before

```sql
ticker VARCHAR(20) NOT NULL UNIQUE
```

### `etf_documents` — After

```sql
ticker      VARCHAR(20) NOT NULL,
chunk_index INT NOT NULL DEFAULT 0,
source      VARCHAR(50) NOT NULL DEFAULT 'manual_seed',
CONSTRAINT uq_etf_documents_ticker_chunk UNIQUE (ticker, chunk_index)
```

### `IChatService` — Before

```csharp
Task<string> AskAiAsync(string question);
```

### `IChatService` — After

```csharp
Task<ChatResponseDto> AskAiAsync(string question, Guid userId, CancellationToken ct = default);
```

### `SearchResult` — Before (class)

```csharp
public class SearchResult { public string Ticker { get; set; } ... }
```

### `SearchResult` — After (record)

```csharp
public sealed record SearchResult { public required string Ticker { get; init; } ... }
```

## Migration Strategy

1. Run `src/db/11_etf_documents_multi_chunk.sql` against the database.
2. The existing 13 seed rows remain valid (they get `chunk_index = 0`, `source = 'manual_seed'`).
3. Once the Airflow DAG runs, factsheet chunks are ingested via delete-and-replace — coexisting with any remaining manual_seed rows for tickers without factsheets.

## Considerations & Trade-offs

| Optimizes For | Sacrifices |
|---|---|
| Privacy (all local, no external API calls) | Cannot use more capable cloud embedding models |
| Simplicity (API key auth) | Not suitable for multi-tenant M2M auth at scale |
| Data freshness (delete-and-replace) | Brief window during replace where ticker has no chunks |
| Determinism (pre-calculated portfolio data) | Extra DB query per chat request for portfolio context |
| Prompt quality (similarity threshold) | May return no results if threshold is too aggressive |

**Decision: `pdfplumber` over `PyMuPDF`** — pdfplumber handles table extraction better for financial factsheets. PyMuPDF is faster but factsheets are small (1-4 pages), so speed is irrelevant.

**Decision: Embedding in Python, not .NET** — Batch embedding during Airflow ingestion keeps the API stateless for ingest. The .NET API only calls Ollama for query-time embedding (single call, fast).

**Decision: No Polly/circuit-breaker on .NET side** — The .NET API does not call Ollama during ingestion (Python does). For query-time embedding (single call), the existing `HttpClient` timeout (30s) is sufficient. If Ollama is down, the user gets an error immediately. Polly adds complexity without value for a single-call pattern on a local service. The Python side handles retries via Airflow's built-in retry mechanism (`retries: 1, retry_delay: 10min` in the DAG).

## Todo List

- [x] Phase 1: Schema & Infrastructure
  - [x] Task 1.1: Create `src/db/11_etf_documents_multi_chunk.sql` migration script
  - [x] Task 1.2: Run migration against local database
  - [x] Task 1.3: Add `IngestApiKey`, `MinSimilarityThreshold`, `MaxContextChunks` to `AISettings`
  - [x] Task 1.4: Update `appsettings.json` and `appsettings.Development.json`
  - [x] Task 1.5: Create `ApiKeyRequiredAttribute`
  - [x] Task 1.6: Create `ApiKeyMiddleware`
  - [x] Task 1.7: Register middleware and `IHttpContextAccessor` in `Program.cs`
  - [x] Task 1.8: Update `docker-compose.yml` with `AI__IngestApiKey` env var

- [ ] Phase 2: Core Interface & DTO Changes
  - [x] Task 2.1: Convert `SearchResult` to sealed record
  - [x] Task 2.2: Create `IngestRequestDto` and `IngestChunkDto`
  - [x] Task 2.3: Create `ChatResponseDto` and `SearchResultDto`
  - [x] Task 2.4: Add `CancellationToken` to `IEmbeddingGenerator`
  - [x] Task 2.5: Add `BulkReplaceAsync`, `CancellationToken`, and `minSimilarity` to `ISemanticSearchRepository`
  - [x] Task 2.6: Change `IChatService.AskAiAsync` to return `ChatResponseDto` with `userId` and `CancellationToken`

- [ ] Phase 3: Infrastructure Implementations
  - [ ] Task 3.1: Update `OllamaEmbeddingService` to propagate `CancellationToken`
  - [ ] Task 3.2: Implement `BulkReplaceAsync` in `DapperSemanticSearchRepository`
  - [ ] Task 3.3: Add similarity threshold and `CancellationToken` to `SearchAsync`
  - [ ] Task 3.4: Refactor `OllamaChatService` — return `ChatResponseDto`, inject portfolio context, fix prompt formatting, propagate `CancellationToken`

- [ ] Phase 4: API Layer
  - [ ] Task 4.1: Create `IngestController` with `POST /api/search/ingest`
  - [ ] Task 4.2: Simplify `ChatController` — remove duplicate dependencies, use only `IChatService`
  - [ ] Task 4.3: Remove `SeedData` endpoint from `SemanticSearchController`
  - [ ] Task 4.4: Add `CancellationToken` to `SemanticSearchController.Query`

- [ ] Phase 5: Python Airflow Pipeline
  - [ ] Task 5.1: Add `pdfplumber` and `httpx` to `airflow/requirements.txt`
  - [ ] Task 5.2: Create `airflow/include/transforms/factsheet_chunker.py`
  - [ ] Task 5.3: Add `get_downloaded_factsheets()` to `ETFDatabaseHook`
  - [ ] Task 5.4: Add `parse_and_embed` task to `etf_knowledge_builder` DAG
  - [ ] Task 5.5: Wire task dependency: `retrieve_factsheets >> parse_and_embed`

- [ ] Phase 6: Verification
  - [ ] Task 6.1: Run `dotnet build` — zero errors
  - [ ] Task 6.2: Run existing tests — zero regressions
  - [ ] Task 6.3: Manual test: POST to `/api/search/ingest` with API key
  - [ ] Task 6.4: Manual test: POST to `/api/chat` with a question and verify sources are returned
  - [ ] Task 6.5: Manual test: Trigger Airflow DAG and verify chunks appear in `etf_documents`

---

**Do not implement yet.**


using EtfInsight.Core.DTOs;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using EtfInsight.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace EtfInsight.Tests.Services;

public sealed class BrokerPdfImportServiceTests
{
    private static readonly Guid PortfolioId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static BrokerPdfImportService BuildService(StubBrokerImportRepository repo)
        => new(
            repo,
            new StubPortfolioRepository(),
            new StubPdfTextExtractor(),
            new StubTradeRepublicParser(),
            new StubInstrumentResolutionService(),
            new StubIngestionService(),
            NullLogger<BrokerPdfImportService>.Instance,
            new NullConfiguration());

    [Fact]
    public async Task GetJobStatusAsync_returns_null_when_job_not_found()
    {
        var repo = new StubBrokerImportRepository();
        var service = BuildService(repo);

        var result = await service.GetJobStatusAsync(JobId, UserId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobStatusAsync_returns_queued_status_when_job_has_no_waiting_items()
    {
        var job = MakeJob("queued");
        var repo = new StubBrokerImportRepository(job);
        var service = BuildService(repo);

        var result = await service.GetJobStatusAsync(JobId, UserId);

        Assert.NotNull(result);
        Assert.Equal(JobId, result.JobId);
        Assert.Equal("queued", result.Status);
    }

    [Fact]
    public async Task GetJobStatusAsync_transitions_to_completed_when_all_waiting_tickers_are_ready()
    {
        var job = MakeJob("waiting_for_ingestion");
        var item = MakeItem("waiting_for_ingestion", resolvedTicker: "SWDA.MI");
        var repo = new StubBrokerImportRepository(
            job,
            items: [item],
            tickerStatuses: new Dictionary<string, string> { ["SWDA.MI"] = "ready" });
        var service = BuildService(repo);

        await service.GetJobStatusAsync(JobId, UserId);

        Assert.Equal("completed", repo.FinalStatus);
    }

    [Fact]
    public async Task GetJobStatusAsync_transitions_to_completed_with_errors_when_other_items_failed()
    {
        var job = MakeJob("waiting_for_ingestion");

        var waitingItem = MakeItem("waiting_for_ingestion", resolvedTicker: "SWDA.MI");
        var failedItem = MakeItem("failed", resolvedTicker: null);
        var repo = new StubBrokerImportRepository(
            job,
            items: [waitingItem, failedItem],
            tickerStatuses: new Dictionary<string, string> { ["SWDA.MI"] = "ready" });
        var service = BuildService(repo);

        await service.GetJobStatusAsync(JobId, UserId);

        Assert.Equal("completed_with_errors", repo.FinalStatus);
    }

    [Fact]
    public async Task GetJobStatusAsync_does_not_transition_when_ticker_is_still_ingesting()
    {
        var job = MakeJob("waiting_for_ingestion");
        var item = MakeItem("waiting_for_ingestion", resolvedTicker: "SWDA.MI");
        var repo = new StubBrokerImportRepository(
            job,
            items: [item],
            tickerStatuses: new Dictionary<string, string> { ["SWDA.MI"] = "ingesting" });
        var service = BuildService(repo);

        await service.GetJobStatusAsync(JobId, UserId);

        Assert.Null(repo.FinalStatus);
    }

    [Fact]
    public async Task CleanupStaleTempFoldersAsync_does_nothing_when_temp_root_does_not_exist()
    {
        var repo = new StubBrokerImportRepository();
        var service = new BrokerPdfImportService(
            repo,
            new StubPortfolioRepository(),
            new StubPdfTextExtractor(),
            new StubTradeRepublicParser(),
            new StubInstrumentResolutionService(),
            new StubIngestionService(),
            NullLogger<BrokerPdfImportService>.Instance,
            new KeyValueConfiguration("BrokerImport:TempRoot", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));

        await service.CleanupStaleTempFoldersAsync();
    }

    [Fact]
    public async Task CleanupStaleTempFoldersAsync_removes_folders_older_than_24_hours()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "broker-import-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);

        var staleFolder = Path.Combine(tempRoot, "stale-" + Guid.NewGuid());
        Directory.CreateDirectory(staleFolder);

        Directory.SetCreationTimeUtc(staleFolder, DateTime.UtcNow.AddHours(-25));

        var recentFolder = Path.Combine(tempRoot, "recent-" + Guid.NewGuid());
        Directory.CreateDirectory(recentFolder);

        try
        {
            var service = new BrokerPdfImportService(
                new StubBrokerImportRepository(),
                new StubPortfolioRepository(),
                new StubPdfTextExtractor(),
                new StubTradeRepublicParser(),
                new StubInstrumentResolutionService(),
                new StubIngestionService(),
                NullLogger<BrokerPdfImportService>.Instance,
                new KeyValueConfiguration("BrokerImport:TempRoot", tempRoot));

            await service.CleanupStaleTempFoldersAsync();

            Assert.False(Directory.Exists(staleFolder));
            Assert.True(Directory.Exists(recentFolder));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static BrokerImportJob MakeJob(string status) => new()
    {
        Id = JobId,
        PortfolioId = PortfolioId,
        UserId = UserId,
        Broker = "trade_republic",
        Status = status,
        TotalFiles = 1,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static BrokerImportJobItem MakeItem(string status, string? resolvedTicker) => new()
    {
        Id = Guid.NewGuid(),
        JobId = JobId,
        PortfolioId = PortfolioId,
        OriginalFileName = "test.pdf",
        TempFilePath = "/tmp/test.pdf",
        FileSha256 = "abc123",
        Status = status,
        ResolvedTicker = resolvedTicker,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private sealed class StubBrokerImportRepository(
        BrokerImportJob? job = null,
        IReadOnlyList<BrokerImportJobItem>? items = null,
        IReadOnlyDictionary<string, string>? tickerStatuses = null) : IBrokerImportRepository
    {
        private BrokerImportJob? _job = job;
        private IReadOnlyList<BrokerImportJobItem> _items = items ?? [];
        private readonly IReadOnlyDictionary<string, string> _tickerStatuses =
            tickerStatuses ?? new Dictionary<string, string>();

        public string? FinalStatus { get; private set; }

        public Task<BrokerImportJob?> GetJobAsync(Guid jobId, Guid userId, CancellationToken ct = default)
            => Task.FromResult(_job);

        public Task<IReadOnlyList<BrokerImportJobItem>> GetItemsAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(_items);

        public Task<IReadOnlyDictionary<string, string>> GetTickerStatusesForJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(_tickerStatuses);

        public Task UpdateItemAsync(BrokerImportJobItem item, CancellationToken ct = default)
        {
            _items = _items
                .Select(i => i.Id == item.Id ? item : i)
                .ToList();
            return Task.CompletedTask;
        }

        public Task UpdateJobCountersAsync(Guid jobId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkJobCompletedAsync(Guid jobId, string finalStatus, string? errorSummary = null, CancellationToken ct = default)
        {
            FinalStatus = finalStatus;
            _job = _job is null ? null : _job with { Status = finalStatus, CompletedAt = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task CreateJobAsync(BrokerImportJob job, IEnumerable<BrokerImportJobItem> items, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateJobStatusAsync(Guid jobId, string status, string? currentFileName = null, string? currentMessage = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> IsDocumentAlreadyImportedAsync(Guid portfolioId, string documentHash, string? brokerReference, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<Guid> InsertBrokerTransactionAsync(BrokerTransactionInsertRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class StubPortfolioRepository : IPortfolioRepository
    {
        public Task<Portfolio?> GetByIdAndUserAsync(Guid portfolioId, Guid userId, CancellationToken ct = default)
            => Task.FromResult<Portfolio?>(null);

        public Task<Portfolio?> GetPortfolioWithTransactionsAsync(Guid id, Guid userId = default)
            => Task.FromResult<Portfolio?>(null);

        public Task<IEnumerable<Portfolio>> GetAllPortfoliosWithTransactionsAsync(Guid userId)
            => Task.FromResult(Enumerable.Empty<Portfolio>());

        public Task BulkAddTransactionsAsync(Guid portfolioId, IEnumerable<Transaction> transactions)
            => Task.CompletedTask;
    }

    private sealed class StubPdfTextExtractor : IPdfTextExtractor
    {
        public Task<PdfExtractionResult> ExtractTextAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(new PdfExtractionResult(null, string.Empty));
    }

    private sealed class StubTradeRepublicParser : ITradeRepublicParser
    {
        public TradeRepublicParserResult Parse(PdfExtractionResult extraction)
            => new TradeRepublicParserResult.Failure("Not implemented in stub", "stub");
    }

    private sealed class StubInstrumentResolutionService : IInstrumentResolutionService
    {
        public Task<string?> ResolveTickerByIsinAsync(string isin, string? instrumentName = null, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubIngestionService : IIngestionService
    {
        public Task<IngestionStatus> EnsureTickerReadyAsync(string ticker, CancellationToken ct = default)
            => Task.FromResult(IngestionStatus.Ready);

        public Task<IngestionStatus> EnsureTickerReadyAsync(string ticker, string? isin, string? name, CancellationToken ct = default)
            => Task.FromResult(IngestionStatus.Ready);
    }

    private sealed class NullConfiguration : IConfiguration
    {
        public string? this[string key] { get => null; set { } }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => new NullSection(key);

        private sealed class NullSection(string key) : IConfigurationSection
        {
            public string? this[string k] { get => null; set { } }
            public string Key => key;
            public string Path => key;
            public string? Value { get => null; set { } }
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
            public IConfigurationSection GetSection(string k) => new NullSection(k);
        }
    }

    private sealed class KeyValueConfiguration(string key, string value) : IConfiguration
    {
        public string? this[string k] { get => k == key ? value : null; set { } }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string k) => new NullSection(k);

        private sealed class NullSection(string key) : IConfigurationSection
        {
            public string? this[string k] { get => null; set { } }
            public string Key => key;
            public string Path => key;
            public string? Value { get => null; set { } }
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
            public IConfigurationSection GetSection(string k) => new NullSection(k);
        }
    }
}
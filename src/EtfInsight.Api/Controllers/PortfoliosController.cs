using System.Data;
using System.Security;
using Dapper;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.Services;
using Microsoft.AspNetCore.Mvc;
using EtfInsight.Api.Extensions;


namespace EtfInsight.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PortfoliosController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IEtfPriceRepository _etfPriceRepository;
    private readonly IPortfolioAnalyticsService _portfolioAnalyticsService;
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<PortfoliosController> _logger;

    public PortfoliosController(
        IDbConnection db,
        IPortfolioRepository portfolioRepository,
        IEtfPriceRepository etfPriceRepository,
        IPortfolioAnalyticsService portfolioAnalyticsService,
        IIngestionService ingestionService,
        ILogger<PortfoliosController> logger)
    {
        _db = db;
        _portfolioRepository = portfolioRepository;
        _etfPriceRepository = etfPriceRepository;
        _portfolioAnalyticsService = portfolioAnalyticsService;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Get all portfolios
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var userId = HttpContext.GetGuestId();

        var portfolios = await _portfolioRepository.GetAllPortfoliosWithTransactionsAsync(userId);

        return Ok(portfolios);
    }

    /// <summary>
    /// Get a portfolio by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (id == Guid.Empty)
        {
            return BadRequest(new { Error = "Invalid portfolio ID." });
        }

        var userId = HttpContext.GetGuestId();

        var portfolio = await _portfolioRepository.GetPortfolioWithTransactionsAsync(id, userId);

        return portfolio is null
            ? NotFound(new { Error = $"Portfolio with ID {id} not found." })
            : Ok(new { portfolio });
    }

    /// <summary>
    /// Create a new portfolio
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] PortfolioCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { Error = "Portfolio name is required." });
        }

        var userId = HttpContext.GetGuestId();

        var query = @"
            INSERT INTO portfolios (name, currency, user_id)
            VALUES (@Name, @Currency, @UserId)
            RETURNING id, name, currency, created_at";

        var portfolio = await _db.QueryFirstAsync(query, new
        {
            Name = request.Name,
            Currency = request.BaseCurrency ?? "EUR",
            UserId = userId
        });

        return CreatedAtAction(nameof(GetById), new { id = portfolio.id }, new { portfolio });

    }

    /// <summary>
    /// Get portfolio transactions
    /// </summary>
    [HttpGet("{portfolioId:int}/transactions")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactions(int portfolioId)
    {
        var userId = HttpContext.GetGuestId();
        var portfolioExists = await _db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
            new { Id = portfolioId });

        if (!portfolioExists)
        {
            return NotFound(new { Error = $"Portfolio with ID {portfolioId} not found." });
        }

        var query = @"
            SELECT id, portfolio_id, symbol, transaction_type, quantity,
                   price_per_unit, fees, transaction_currency, transaction_date, notes
            FROM transactions
            WHERE portfolio_id = @PortfolioId
            ORDER BY transaction_date DESC";

        var transactions = await _db.QueryAsync(query, new { PortfolioId = portfolioId });
        return Ok(new { transactions });
    }

    /// <summary>
    /// Add a transaction to a portfolio
    /// </summary>
    [HttpPost("{portfolioId:int}/transactions")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddTransaction(
        int portfolioId,
        [FromBody] TransactionCreateRequest request,
        CancellationToken ct = default)
    {
        // -- Validation --
        var ticker = request.Ticker.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return BadRequest(new { Error = "Ticker is required." });
        }

        if (request.Units <= 0)
        {
            return BadRequest(new { Error = "Units must be greater than zero." });
        }

        if (request.PricePerUnit < 0)
        {
            return BadRequest(new { Error = "PricePerUnit cannot be negative." });
        }

        var validTypes = new[] { "BUY", "SELL", "DEPOSIT", "WITHDRAW" };
        if (!validTypes.Contains(request.Type.ToUpperInvariant()))
        {
            return BadRequest(new
            {
                Error = $"Type must be one of: {string.Join(", ", validTypes)}"
            });
        }

        var portfolioExists = await _db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
            new { Id = portfolioId });

        if (!portfolioExists)
        {
            return NotFound(new { Error = $"Portfolio {portfolioId} not found." });
        }


        // -- JIT: ensure ticker has (or is getting) price data before the FK insert below --
        var ingestionStatus = await _ingestionService.EnsureTickerReadyAsync(ticker, ct);


        // -- Insert transaction regardless of ingestion status.
        // The etf_metadata placeholder row is already committed by EnsureTickerReadyAsync
        // so the FK constraint is satisfied --

        var transactionDate = request.TransactionDate?.Date ?? DateTime.UtcNow.Date;


        var transaction = await _db.QueryFirstAsync("""
        INSERT INTO transactions
            (portfolio_id, ticker, type, units, price_per_unit, fees, transaction_date)
        VALUES
            (@PortfolioId, @Ticker, @Type, @Units, @PricePerUnit, @Fees, @TransactionDate)
        RETURNING id, portfolio_id, ticker, type, units, price_per_unit, fees, transaction_date
        """,
        new
        {
            PortfolioId = portfolioId,
            Ticker = ticker,
            Type = request.Type.ToUpperInvariant(),
            Units = request.Units,
            PricePerUnit = request.PricePerUnit,
            Fees = request.Fees,
            TransactionDate = transactionDate,
        });

        return ingestionStatus == IngestionStatus.Ready
         ? CreatedAtAction(nameof(GetById), new { id = portfolioId }, new
         {
             transaction,
             ingestion = new { status = "ready" },
             message = $"Transaction added with ticker {ticker}. Price data already available."
         })
         : Accepted(new
         {
             transaction,
             ingestion = new
             {
                 status = ingestionStatus.ToString().ToLower(),   // "ingesting" or "error"
                 ticker,
                 message = ingestionStatus == IngestionStatus.Ingesting
                       ? $"Fetching price history for {ticker}. Analytics will be available shortly."
                       : $"Failed to start price ingestion for {ticker}. Transaction saved; retry ingestion later."
             }
         });
    }


    [HttpGet("{id}/analytics/valuation/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetValuationHistory(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var dashboard = await _portfolioAnalyticsService.GetPortfolioAnalyticsAsync(
            id,
            from ?? DateOnly.Parse("2000-01-01"),
            to ?? DateOnly.FromDateTime(DateTime.UtcNow)
        );

        // Simple filtering in memory (for now ok, then can be optimized in service or DB)
        var history = dashboard.History
            .Where(d => (!from.HasValue || d.Date >= from.Value) && (!to.HasValue || d.Date <= to.Value))
            .OrderBy(d => d.Date);

        return Ok(history.ToList());
    }
    /// <summary>
    /// Get portfolio dashboard summary
    /// </summary>
    [HttpGet("{id}/analytics/dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var dashboard = await _portfolioAnalyticsService.GetPortfolioAnalyticsAsync(
            id,
            from ?? DateOnly.Parse("2000-01-01"),
            to ?? DateOnly.FromDateTime(DateTime.UtcNow)
        );

        return Ok(dashboard);
    }

    /// <summary>
    /// Get portfolio TWRR summary. Defaults to YTD (1 Jan of current year → today).
    /// </summary>
    [HttpGet("{id}/analytics/summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPortfolioPerformance(
        Guid id,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = from != null
            ? DateOnly.Parse(from)
            : new DateOnly(today.Year, 1, 1);   // 01/01/current-year
        var toDate = to != null
            ? DateOnly.Parse(to)
            : today;

        var twrr = await _portfolioAnalyticsService.CalculateTWRR(id, fromDate, toDate);

        return Ok(new
        {
            portfolioId = id,
            twrrYtd = Math.Round(twrr, 4),
            twrrYtdPercentage = $"{Math.Round(twrr * 100, 2)}%",
            analysisPeriod = new { From = fromDate.ToString("yyyy-MM-dd"), To = toDate.ToString("yyyy-MM-dd") },
        });
    }

    public record PortfolioCreateRequest(
        string Name,
        string? Description,
        string? BaseCurrency
    );

    public record TransactionCreateRequest(
        string Ticker,
        string Type,
        decimal Units,
        decimal PricePerUnit,
        decimal? Fees = 0,
        string? Currency = "EUR",
        DateTime? TransactionDate = null
    );

    public record ErrorResponse(string Error);
}

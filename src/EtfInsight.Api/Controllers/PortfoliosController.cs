using System.Data;
using Dapper;
using EtfInsight.Core.Entities;
using EtfInsight.Core.Interfaces;
using EtfInsight.Core.Services;
using Microsoft.AspNetCore.Mvc;


namespace EtfInsight.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PortfoliosController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IEtfPriceRepository _etfPriceRepository;
    private readonly IPerformanceCalculator _performanceCalculator;
    private readonly ILogger<PortfoliosController> _logger;

    public PortfoliosController(
        IDbConnection db,
        IPortfolioRepository portfolioRepository,
        IEtfPriceRepository etfPriceRepository,
        IPerformanceCalculator performanceCalculator,
        ILogger<PortfoliosController> logger)
    {
        _db = db;
        _portfolioRepository = portfolioRepository;
        _etfPriceRepository = etfPriceRepository;
        _performanceCalculator = performanceCalculator;
        _logger = logger;
    }

    /// <summary>
    /// Get all portfolios
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var query = @"
            SELECT p.id, p.name, p.description, p.base_currency, p.created_at,
                COUNT(t.id) as transaction_count,
                MIN(t.transaction_date) as first_transaction_date,
                MAX(t.transaction_date) as last_transaction_date
            FROM portfolios p
            LEFT JOIN transactions t on p.id = t.portfolio_id
            GROUP BY p.id, p.name, p.description, p.base_currency, p.created_at
            ORDER BY p.created_at DESC";

        var portfolios = await _db.QueryAsync(query);
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

        var portfolio = await _portfolioRepository.GetPortfolioWithTransactionsAsync(id);

        if (portfolio == null)
        {
            return NotFound(new { Error = $"Portfolio with ID {id} not found." });
        }

        return Ok(new { portfolio });
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

        var query = @"
            INSERT INTO portfolios (name, description, base_currency)
            VALUES (@Name, @Description, @BaseCurrency)
            RETURNING id, name, description, base_currency, created_at";

        var portfolio = await _db.QueryFirstAsync(query, new
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            BaseCurrency = request.BaseCurrency ?? "USD"
        });

        return CreatedAtAction(
            nameof(GetById),
            new { id = portfolio.id },
            portfolio);
    }

    /// <summary>
    /// Get portfolio transactions
    /// </summary>
    [HttpGet("{portfolioId:int}/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactions(int portfolioId)
    {
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
        return Ok(transactions);
    }

    /// <summary>
    /// Add a transaction to a portfolio
    /// </summary>
    [HttpPost("{portfolioId:int}/transactions")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddTransaction(
        int portfolioId,
        [FromBody] TransactionCreateRequest request)
    {
        var portfolioExists = await _db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
            new { Id = portfolioId });

        if (!portfolioExists)
        {
            return NotFound(new { Error = $"Portfolio with ID {portfolioId} not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            return BadRequest(new { Error = "Symbol is required." });
        }

        if (request.Quantity <= 0)
        {
            return BadRequest(new { Error = "Quantity must be greater than zero." });
        }

        if (request.Price < 0)
        {
            return BadRequest(new { Error = "Price cannot be negative." });
        }

        var validTypes = new[] { "BUY", "SELL", "DEPOSIT", "WITHDRAW" };
        if (!validTypes.Contains(request.TransactionType.ToUpper()))
        {
            return BadRequest(new
            {
                Error = $"Invalid transaction type. Must be one of: {string.Join(", ", validTypes)}"
            });
        }

        var query = @"
            INSERT INTO transactions (
                portfolio_id, symbol, transaction_type, quantity,
                price_per_unit, fees, transaction_currency, transaction_date, notes
            )
            VALUES (
                @PortfolioId, @Symbol, @TransactionType::transaction_type, @Quantity,
                @PricePerUnit, @Fees, @Currency, @TransactionDate, @Notes
            )
            RETURNING id, portfolio_id, symbol, transaction_type, quantity,
                      price_per_unit, fees, transaction_currency, transaction_date, notes";

        var transaction = await _db.QueryFirstAsync(query, new
        {
            PortfolioId = portfolioId,
            Symbol = request.Symbol,
            TransactionType = request.TransactionType.ToUpper(),
            Quantity = request.Quantity,
            PricePerUnit = request.Price,
            Fees = 0m,
            Currency = request.Currency ?? "USD",
            TransactionDate = request.TransactionDate,
            Notes = request.Notes
        });

        return CreatedAtAction(
            nameof(GetTransactions),
            new { portfolioId },
            transaction);
    }

    /// <summary>
    /// Get portfolio dashboard summary
    /// </summary>
    [HttpGet("{portfolioId:int}/dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard(int portfolioId)
    {
        var portfolioExists = await _db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM portfolios WHERE id = @Id)",
            new { Id = portfolioId });

        if (!portfolioExists)
        {
            return NotFound(new { Error = $"Portfolio with ID {portfolioId} not found." });
        }

        var portfolioQuery = @"
            SELECT id, name, description, base_currency, created_at
            FROM portfolios
            WHERE id = @Id";

        var portfolio = await _db.QuerySingleAsync(portfolioQuery, new { Id = portfolioId });
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var holdingsQuery = @"
            SELECT
                symbol,
                SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) as total_quantity
            FROM transactions
            WHERE portfolio_id = @PortfolioId
            AND transaction_date <= @Today
            GROUP BY symbol
            HAVING SUM(CASE WHEN transaction_type = 'BUY' THEN quantity ELSE -quantity END) > 0";

        var holdings = await _db.QueryAsync(holdingsQuery, new
        {
            PortfolioId = portfolioId,
            Today = DateTime.Parse(today)
        });

        var currentValue = 0m;
        var holdingDetails = new List<object>();

        foreach (var holding in holdings)
        {
            string symbol = (string)holding.symbol;
            decimal quantity = (decimal)holding.total_quantity;

            var priceQuery = @"
                SELECT close_price
                FROM etf_prices
                WHERE symbol = @Symbol
                    AND price_date <= @Today
                ORDER BY price_date DESC
                LIMIT 1";

            var priceRecord = await _db.ExecuteScalarAsync<decimal?>(priceQuery, new
            {
                Symbol = symbol,
                Today = DateTime.Parse(today)
            });

            if (priceRecord != null)
            {
                var value = quantity * priceRecord.Value;
                currentValue += value;

                holdingDetails.Add(new
                {
                    symbol,
                    quantity = Math.Round(quantity, 2),
                    price = Math.Round(priceRecord.Value, 2),
                    value = Math.Round(value, 2),
                    allocation_percent = 0.00m
                });
            }
        }

        var updatedHoldings = holdingDetails.Select(h =>
        {
            dynamic hDyn = h;
            var value = (decimal)hDyn.value;
            return new
            {
                hDyn.symbol,
                hDyn.quantity,
                hDyn.price,
                hDyn.value,
                allocation_percent = currentValue > 0
                    ? Math.Round((value / currentValue) * 100, 2)
                    : 0.00m
            };
        }).ToList();

        holdingDetails = updatedHoldings.Cast<object>().ToList();

        return Ok(new
        {
            portfolio,
            current_value = Math.Round(currentValue, 2),
            holdings = holdingDetails
        });
    }

    /// <summary>
    /// Get portfolio performance
    /// </summary>

    [HttpGet("{id}/performance")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPortfolioPerformance(
    Guid id,
    string? from,
    string? to,
    IPortfolioRepository repository,
    IEtfPriceRepository etfPriceRepository,
    IPerformanceCalculator performanceCalculator,
    IDbConnection db)
    {
        var portfolio = await repository.GetPortfolioWithTransactionsAsync(id);

        if (portfolio == null)
        {
            return NotFound(new ErrorResponse($"Portfolio with ID {id} not found."));
        }

        if (portfolio.Transactions == null || !portfolio.Transactions.Any())
        {
            return Ok(new
            {
                PortfolioId = id,
                AnalysisPeriod = new { From = from ?? "2000-01-01", To = to ?? DateTime.UtcNow.ToString("yyyy-MM-dd") },
                Twrr = 0m,
                TwrrPercentage = "0.00%",
                DataPoints = 0
            });
        }

        var etfTickers = portfolio.Transactions
            .Select(t => t.Ticker)
            .Distinct()
            .ToList();

        var etfPrices = await etfPriceRepository.GetPricesByTickersAsync(
            etfTickers,
            DateOnly.Parse(from ?? "2000-01-01"),
            DateOnly.Parse(to ?? DateTime.UtcNow.ToString("yyyy-MM-dd"))
        );

        var twrr = performanceCalculator.CalculateTWRR(
            portfolio.Transactions,
                etfPrices);

        return Ok(new
        {
            portfolioId = id,
            twrr = Math.Round(twrr, 4),
            twrrPercentage = $"{Math.Round(twrr * 100, 2)}%",
            dataPoints = etfPrices.Count(),
            analysisPeriod = new { From = from ?? "2000-01-01", To = to ?? DateTime.UtcNow.ToString("yyyy-MM-dd") },
        });
    }

    public record PortfolioCreateRequest(
        string Name,
        string? Description,
        string? BaseCurrency
    );

    public record TransactionCreateRequest(
        string Symbol,
        string TransactionType,
        decimal Quantity,
        decimal Price,
        string? Currency,
        DateTime TransactionDate,
        string? Notes
    );
}

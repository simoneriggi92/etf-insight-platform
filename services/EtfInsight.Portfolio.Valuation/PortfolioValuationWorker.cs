namespace EtfInsight.Portfolio.Valuation;

public class PortfolioValuationWorker : BackgroundService
{
    private readonly ILogger<PortfolioValuationWorker> _logger;
    private readonly IConfiguration _configuration;

    public PortfolioValuationWorker(ILogger<PortfolioValuationWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Portfolio Valuation Worker starting.");

        try
        {
            await ValuatePortfoliosAsync(stoppingToken);   
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "An error occurred during portfolio valuation.");
        }
        

        _logger.LogInformation("Portfolio Valuation Worker stopping.");
    }

    private async Task ValuatePortfoliosAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string is not configured.");

        _logger.LogInformation("Starting portfolio valuation process.");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Get list of portfolios
        var portfolioCmd = new NpgsqlCommand("select id, name from portfolio", (NpgsqlConnection)conn);
        await using var portfolioReader = await portfolioCmd.ExecuteReaderAsync(cancellationToken);

        var portfolios = new List<Portfolio>();

        while (await portfolioReader.ReadAsync(cancellationToken))
        {
            portfolios.Add(new Portfolio
            {
                Id = portfolioReader.GetInt32(0),
                Name = portfolioReader.GetString(1)
            });
        }

        // For each portfolio, for each date, calculate valuation
        foreach(var portfolio in portfolios)
        {
            // get the price dates from the etf_prices_history table
            var dateCmd = new NpgsqlCommand(@"
                select 
                    distinct price_date 
                from 
                    etf_price_history eph
				inner join 
                    etf e on eph.etf_id = e.id      
                order by 
                    price_date", (NpgsqlConnection)conn);

            await using var dateReader = await dateCmd.ExecuteReaderAsync(cancellationToken);
            var priceDates = new List<DateTime>();
            while (await dateReader.ReadAsync(cancellationToken))
            {
                priceDates.Add(dateReader.GetDateTime(0));
            }
        }

        // Now we have portfolios and price dates, we can calculate valuations
        var positionsSql = @"
            with positions as (
                select
                    pt.portfolio_id,
                    pt.etf_id,
                    sum(case 
                            when pt.trade_type = 'BUY' then pt.quantity
                            when pt.trade_type = 'SELL' then -pt.quantity
                            else 0
                        end) as quantity
                from portfolio_transaction pt
                where pt.portfolio_id = @portfolio_id
                and pt.trade_date <= @as_of
                group by pt.portfolio_id, pt.etf_id
            ),
            prices as (
                select
                    eph.etf_id,
                    eph.price_date,
                    eph.close_price
                from etf_price_history eph
                where eph.price_date = @as_of
            )
            select
                p.portfolio_id,
                e.ticker,
                p.quantity,
                pr.close_price
            from positions p
            join etf e on e.id = p.etf_id
            left join prices pr on pr.etf_id = p.etf_id
            where p.quantity <> 0
            order by e.ticker;
        ";

        var positionsCmd = new NpgsqlCommand(
        positionsSql, 
        (NpgsqlConnection)conn);

        List<PortfolioValuation> valuations = new List<PortfolioValuation>();
        // create a dictionary with id and dates to evaluate
        var portfolioDates = new Dictionary<int, List<DateTime>>();
        foreach(var portfolio in portfolios)
        {
            portfolioDates[portfolio.Id] = priceDates;
        }

        // Iterate over each portfolio and date to evaluate
        foreach(var kvp in portfolioDates)
        {
            var id = kvp.Key;
            var dates = kvp.Value;

            foreach(var asOfDate in dates)
            {
                positionsCmd.Parameters.Clear();
                positionsCmd.Parameters.AddWithValue("portfolio_id", id);
                positionsCmd.Parameters.AddWithValue("as_of", asOfDate);

                await using var positionsReader = await positionsCmd.ExecuteReaderAsync(cancellationToken);

                var positions = new List<Position>();
                decimal totalValue = 0;

                while(await positionsReader.ReadAsync(cancellationToken))
                {
                    var ticker = positionsReader.GetString(1);
                    var quantity = positionsReader.GetDecimal(2);
                    decimal? closePrice = positionsReader.IsDBNull(3) ? null : positionsReader.GetDecimal(3);
                    decimal? positionValue = null;

                    if(closePrice.HasValue)
                    {
                        positionValue = quantity * closePrice.Value;
                        totalValue += positionValue.Value;
                    }

                    positions.Add(new Position
                    {
                        Ticker = ticker,
                        Quantity = quantity,
                        ClosePrice = closePrice,
                        Value = positionValue
                    });
                }

                // Here you would typically store or process the valuation result
                var valuation = new PortfolioValuation
                {
                    Portfolio = portfolios.First(p => p.Id == id),
                    AsOfDate = asOfDate,
                    Positions = positions.Select(p => new Position
                    {
                        Ticker = (string)p.GetType().GetProperty("Ticker")!.GetValue(p)!,
                        Quantity = (decimal)p.GetType().GetProperty("Quantity")!.GetValue(p)!,
                        ClosePrice = p.GetType().GetProperty("ClosePrice")!.GetValue(p) is decimal cp ? cp : 0,
                        Value = p.GetType().GetProperty("Value")!.GetValue(p) is decimal v ? (int)v : 0
                    }).ToList(),
                    TotalValue = totalValue
                };
                valuations.Add(valuation);
            }
        }
        
        

        // Placeholder for portfolio valuation logic
        _logger.LogInformation("Portfolio valuation process completed.");
    }

    private class PortfolioValuation
    {
        public Portfolio Portfolio { get; set; } = new Portfolio();
        public DateTime AsOfDate { get; set; }
        public List<Position> Positions { get; set; } = new List<Position>();
        public decimal TotalValue { get; set; }
    }

    private class Portfolio 
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class Position
    {
        public string Ticker { get; set; }
        public decimal Quantity { get; set; }
        public decimal ClosePrice { get; set; }
        public int Value { get; set; }
    }
}

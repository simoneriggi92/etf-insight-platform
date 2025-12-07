namespace EtfInsight.Portfolio.Valuation;
using System.Data;
using Npgsql;

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

        var portfolios = new List<Portfolio>();

        await using (var portfolioReader = await portfolioCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await portfolioReader.ReadAsync(cancellationToken))
            {   
                portfolios.Add(new Portfolio
                {
                    Id = portfolioReader.GetInt32(0),
                    Name = portfolioReader.GetString(1)
                });
            }
        } // Reader chiuso qui


         var insertCmd = new NpgsqlCommand(@"
                    insert into portfolio_valuation (portfolio_id, valuation_date, total_value, base_currency, created_at)
                    values (@portfolio_id, @valuation_date, @total_value, @base_currency, now())
                    on conflict on constraint uq_portfolio_valuation do update
                    set total_value = excluded.total_value,
                        created_at = now();", (NpgsqlConnection)conn);

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

           await using (var dateReader = await dateCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await dateReader.ReadAsync(cancellationToken))
                {
                    portfolio.PriceDates.Add(dateReader.GetDateTime(0));
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

            // Iterate over each portfolio and date to evaluate
        
            var id = portfolio.Id;
            var dates = portfolio.PriceDates;

            var positions = new List<Position>();
            foreach(var asOfDate in dates)
            {
                positionsCmd.Parameters.Clear();
                positionsCmd.Parameters.AddWithValue("portfolio_id", id);
                positionsCmd.Parameters.AddWithValue("as_of", asOfDate);

                // decimal totalValue = 0;

                await using (var positionsReader = await positionsCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while(await positionsReader.ReadAsync(cancellationToken))
                    {
                        var ticker = positionsReader.GetString(1);
                        var quantity = positionsReader.GetDecimal(2);
                        decimal? closePrice = positionsReader.IsDBNull(3) ? null : positionsReader.GetDecimal(3);
                        decimal? positionValue = null;

                        if(closePrice.HasValue)
                        {
                            positionValue = quantity * closePrice.Value;
                            // totalValue += positionValue.Value;
                        }

                        portfolio.Positions.Add(new Position
                        {
                            Ticker = ticker,
                            Quantity = quantity,
                            ClosePrice = closePrice,
                            Value = positionValue,
                            AsOfDate = asOfDate
                        });
                    }
                }
            
                // Here you would typically store or process the valuation result
                foreach(var pos in portfolio.Positions.GroupBy(p => p.AsOfDate))
                {
                    var valuationDate = pos.Key;
                    var totalValuation = pos.Sum(p => p.Value ?? 0);

                    portfolio.Valuations.Add(new PortfolioValuation
                    {
                        ValuationDate = valuationDate,
                        TotalValue = totalValuation,
                        BaseCurrency = "EUR",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                foreach(var val in portfolio.Valuations)
                {
                    insertCmd.Parameters.Clear();
                    insertCmd.Parameters.AddWithValue("portfolio_id", portfolio.Id);
                    insertCmd.Parameters.AddWithValue("valuation_date", val.ValuationDate);
                    insertCmd.Parameters.AddWithValue("total_value", val.TotalValue);
                    insertCmd.Parameters.AddWithValue("base_currency", val.BaseCurrency);

                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Clear valuations and positions for the next date
                portfolio.Valuations.Clear();
                portfolio.Positions.Clear();
            }   
        }

        // Placeholder for portfolio valuation logic
        _logger.LogInformation("Portfolio valuation process completed.");
    }

    private class PortfolioValuation
    {
        public DateTime ValuationDate { get; set; }
        public decimal TotalValue { get; set; }
        public string BaseCurrency { get; set; } = "EUR";
        public List<DateTime> PriceDates { get; set; } = new List<DateTime>();
        public DateTime CreatedAt { get; set; }
    }

    private class Portfolio 
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Position> Positions { get; set; } = new List<Position>();
        public List<PortfolioValuation> Valuations { get; set; } = new List<PortfolioValuation>();
        public List<DateTime> PriceDates { get; set; } = new List<DateTime>();
    }

    private class Position
    {
        public string Ticker { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? ClosePrice { get; set; }
        public decimal? Value { get; set; }
        public DateTime AsOfDate { get; set; }
    }
}

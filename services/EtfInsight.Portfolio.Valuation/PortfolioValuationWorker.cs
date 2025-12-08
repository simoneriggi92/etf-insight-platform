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

        var portfolios = await GetPortfoliosAsync(conn, cancellationToken);

        var valuationCmd = GetValuationCmd(conn);

        // Prepare insert command for portfolio valuations
         var insertCmd = GetInsertCmd(conn);

        // For each portfolio, for each date, calculate valuation
        foreach(var portfolio in portfolios)
        {
            if(portfolio.StartDate == null)
            {
                _logger.LogInformation(
                    "Portfolio '{PortfolioName}' ({PortfolioId}) has no transactions. Skipping.", 
                    portfolio.Name,
                    portfolio.Id);

                continue;
            }

            // get the price dates from the etf_prices_history table
            var dates = await GetPriceDatesAsync(portfolio.StartDate.Value , conn, cancellationToken);
            
            _logger.LogInformation(
                "Valuating portfolio '{PortfolioName}' ({PortfolioId}) for {DateCount} dates.", 
                portfolio.Name, 
                portfolio.Id, 
                dates.Count);

            foreach(var asOfDate in dates)
            {
                valuationCmd.Parameters.Clear();
                valuationCmd.Parameters.AddWithValue("portfolio_id", portfolio.Id);
                valuationCmd.Parameters.AddWithValue("as_of", asOfDate.Date);

                // decimal totalValue = 0;
                var totalValueObject = await valuationCmd.ExecuteScalarAsync(cancellationToken);
                var totalValue = TryGetDecimal(totalValueObject) ?? 0m;

                insertCmd.Parameters.Clear();
                insertCmd.Parameters.AddWithValue("portfolio_id", portfolio.Id);
                insertCmd.Parameters.AddWithValue("valuation_date", asOfDate);
                insertCmd.Parameters.AddWithValue("total_value", totalValue);
                insertCmd.Parameters.AddWithValue("base_currency", portfolio.BaseCurrency.ToString());

                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }   
        }

        _logger.LogInformation("Portfolio valuation process completed.");
    }

    private async Task<List<Portfolio>> GetPortfoliosAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
         var portfolioCmd = new NpgsqlCommand(@"
            select
             p.id, p.name, p.base_currency, min (pt.trade_date) as first_transaction_date
             from portfolio p
             left join portfolio_transaction pt on p.id = pt.portfolio_id
             group by p.id, p.name, p.base_currency"
            , (NpgsqlConnection)conn);

        var portfolios = new List<Portfolio>();

        await using (var portfolioReader = await portfolioCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await portfolioReader.ReadAsync(cancellationToken))
            {   
                portfolios.Add(new Portfolio
                {
                    Id = portfolioReader.GetInt32(0),
                    Name = portfolioReader.GetString(1),
                    BaseCurrency = Enum.Parse<Currencies>(portfolioReader.GetString(2), ignoreCase: true),
                    StartDate = portfolioReader.IsDBNull(3) ? null : portfolioReader.GetDateTime(3)
                });
            }
        }
        return portfolios;
    }

    private static NpgsqlCommand GetValuationCmd(NpgsqlConnection connection)
    {
         // Now we have portfolios and price dates, we can calculate valuations
        var valuationSql = @"
            with positions as (
                select
                    pt.etf_id,
                    sum(case 
                            when pt.trade_type = 'BUY' then pt.quantity
                            when pt.trade_type = 'SELL' then -pt.quantity
                            else 0
                        end) as quantity
                from portfolio_transaction pt
                where pt.portfolio_id = @portfolio_id
                and pt.trade_date <= @as_of
                group by pt.etf_id
            ),
            prices as (
                select
                    eph.etf_id,
                    eph.close_price
                from etf_price_history eph
                where eph.price_date = @as_of
            )
            select
                coalesce(sum(p.quantity * pr.close_price), 0) as total_value
            from positions p
            left join prices pr on p.etf_id = pr.etf_id
        ";

        return new NpgsqlCommand(valuationSql, connection);
    }

    private static NpgsqlCommand GetInsertCmd(NpgsqlConnection connection)
    {
        return new NpgsqlCommand(@"
                    insert into portfolio_valuation 
                        (portfolio_id, valuation_date, total_value, base_currency, created_at)
                    values 
                        (@portfolio_id, @valuation_date, @total_value, @base_currency, now())
                    on conflict on constraint uq_portfolio_valuation do update
                    set total_value = excluded.total_value,
                        created_at = now();", connection);
    }
    private static decimal? TryGetDecimal(object dbValue)
    {
        if (dbValue == null || dbValue is DBNull)
            return null;

        return Convert.ToDecimal(dbValue);
    }

    private async Task<List<DateTime>> GetPriceDatesAsync(DateTime startDate, NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        var dateCmd = new NpgsqlCommand(@"
            select 
                distinct price_date
            from 
                etf_price_history eph  
            where
                eph.price_date >= @start_date
            order by 
                price_date", (NpgsqlConnection)conn);

            dateCmd.Parameters.AddWithValue("start_date", startDate.Date);

            var dates = new List<DateTime>();
            await using (var dateReader = await dateCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await dateReader.ReadAsync(cancellationToken))
                {
                    dates.Add(dateReader.GetDateTime(0));
                }
            }
            return dates;
    }

    private class Portfolio 
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Currencies BaseCurrency { get; set; } = Currencies.EUR;
        public DateTime? StartDate { get; set; }
    }
    
    public enum Currencies
    {
        EUR,
        USD,
        GBP,
        JPY,
        CHF
    }
}

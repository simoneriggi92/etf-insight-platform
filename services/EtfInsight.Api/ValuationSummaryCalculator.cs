using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace EtfInsight.Api
{
    public static class ValuationSummaryCalculator
    {
        public static async Task<(string? BaseCurrency, List<ValuationPoint> Points)> LoadValuationHistoryAsync(
            int portfolioId, DateTime? from, DateTime? to, IDbConnectionFactory dbConnectionFactory)
        {
            await using var conn = dbConnectionFactory.CreateConnection();
            await conn.OpenAsync();

            // Check if the portfolio exists
            var portfolioExistsCmd = new NpgsqlCommand(
                "select 1 from portfolio where id = @id",
                (NpgsqlConnection)conn);

            portfolioExistsCmd.Parameters.AddWithValue("id", portfolioId);
            var exists = await portfolioExistsCmd.ExecuteScalarAsync();
            if (exists == null)
            {
                throw new InvalidOperationException($"Portfolio with id '{portfolioId}' does not exist.");
            }

            var maxEvaluationDateSql = @"
                select max(pv.valuation_date)
                from portfolio_valuation pv
                where pv.portfolio_id = @portfolio_id
                ";

            await using var maxEvalCmd = new NpgsqlCommand(maxEvaluationDateSql, (NpgsqlConnection)conn);
            maxEvalCmd.Parameters.AddWithValue("portfolio_id", portfolioId);
            var maxEvalDateObj = await maxEvalCmd.ExecuteScalarAsync();

            if ((maxEvalDateObj == null || maxEvalDateObj == DBNull.Value) && !to.HasValue)
            {
                // No valuations found for the portfolio
                return (null, new List<ValuationPoint>());
            }


            // Get all transactions from the beginning up to 'to' date to calculate net flows
            var transactionsSql = @"
                select (pt.trade_date::date) as trade_date,
                    sum(case 
                            when pt.trade_type = 'BUY' then +pt.total_amount
                            when pt.trade_type = 'SELL'then -pt.total_amount
                            else 0 
                                end) as netFlow
                from portfolio_transaction pt
                where pt.portfolio_id = @portfolio_id
                    and pt.trade_date <= @maxValuationDate
                group by (pt.trade_date::date)
                order by (pt.trade_date::date)
                ";

            // Read and save the net flow per date
            await using var transactionsCmd = new NpgsqlCommand(transactionsSql, (NpgsqlConnection)conn);

            var maxEvalDate = maxEvalDateObj switch
            {
                null or DBNull => DateTime.MaxValue,
                DateTime dt => dt,
                DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
                _ => DateTime.MaxValue
            };

            var maxDate = to.HasValue && to.Value < maxEvalDate
                ? to.Value
                : maxEvalDate;

            transactionsCmd.Parameters.AddWithValue("maxValuationDate", maxDate);
            transactionsCmd.Parameters.AddWithValue("portfolio_id", portfolioId);

            var transactions = new Dictionary<DateTime, decimal>();
            await using (var transactionsReader = await transactionsCmd.ExecuteReaderAsync())
            {
                while (await transactionsReader.ReadAsync())
                {
                    var tradeDate = transactionsReader.GetDateTime(0).Date;
                    var netFlowAmount = transactionsReader.GetDecimal(1);

                    transactions[tradeDate] = netFlowAmount;
                }
            }

            var orderedFlows = transactions
            .OrderBy(kv => kv.Key)
            .ToList();

            var sql = @"
                select
                    pv.base_currency,
                    pv.valuation_date,
                    pv.total_value
                from portfolio_valuation pv
                where pv.portfolio_id = @portfolio_id
                ";

            if (from.HasValue)
            {
                sql += " and pv.valuation_date >= @fromDate ";
            }
            if (to.HasValue)
            {
                sql += " and pv.valuation_date <= @toDate ";
            }

            sql += " order by pv.valuation_date asc";

            var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)conn);
            cmd.Parameters.AddWithValue("portfolio_id", portfolioId);
            if (from.HasValue) cmd.Parameters.AddWithValue("fromDate", from.Value);
            if (to.HasValue) cmd.Parameters.AddWithValue("toDate", to.Value);

            await using var reader = await cmd.ExecuteReaderAsync();

            var points = new List<ValuationPoint>();
            var baseCurrency = null as string;

            var previousValue = 0m;
            var percentChange = 0m;
            var absoluteChange = 0m;
            var cumulativeNetFlow = 0m; // invested net worth =  cumulativeFlow(D) = Σ netFlow(t) fino a D
            var pnL = 0m; // profit/loss market-to-market = totalValue(D) - cumulativeNetFlow(D)
            var performance = 0m; // performance compared to paid-in capital = pnL(D) / cumulativeNetFlow(D)

            var flowIndex = 0;
            while (await reader.ReadAsync())
            {
                if (string.IsNullOrEmpty(baseCurrency) && !reader.IsDBNull(0))
                {
                    baseCurrency = reader.GetString(0);
                }

                // Calculate metrics
                var valuationDate = reader.GetDateTime(1).Date;
                var currentValue = reader.GetDecimal(2);

                // Update cumulative net flow up to and including valuationDate
                var netFlowToday = 0m;  // Σ total_amount (BUY) − Σ total_amount (SELL)

                while (flowIndex < orderedFlows.Count && orderedFlows[flowIndex].Key <= valuationDate)
                {
                    var flowDate = orderedFlows[flowIndex].Key;
                    var flowAmount = orderedFlows[flowIndex].Value;

                    cumulativeNetFlow += flowAmount;

                    if (flowDate == valuationDate)
                    {
                        netFlowToday += flowAmount;
                    }

                    flowIndex++;
                }

                absoluteChange = previousValue != 0 ? currentValue - previousValue : 0;
                // percentChange(D) = (Value(D) - Value(D-1)) / Value(D-1)
                percentChange = previousValue != 0 ? (absoluteChange / previousValue) : 0; // daily change of total value (flows + market), not performance over time

                // netFlow = transactions.TryGetValue(reader.GetDateTime(1).Date, out var flow) ? flow : 0;
                pnL = currentValue - cumulativeNetFlow;
                performance = cumulativeNetFlow != 0 ? (pnL / cumulativeNetFlow) : 0;

                points.Add(new ValuationPoint(
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    MathRound(reader.GetDecimal(2), 2),
                    MathRound(absoluteChange, 2),
                    MathRound(percentChange, 3),
                    MathRound(netFlowToday, 2),
                    MathRound(cumulativeNetFlow, 2),
                    MathRound(pnL, 2),
                    MathRound(performance, 3)
                ));

                previousValue = currentValue;
            }
            return (baseCurrency, points);
        }

        public static ValuationSummary ComputeSummary(IReadOnlyList<ValuationPoint> points, int portfolioId = 0, string? baseCurrency = null)
        {
            if (points == null || points.Count == 0)
            {
                return new ValuationSummary(
                    PortfolioId: portfolioId,
                    BaseCurrency: baseCurrency,
                    HasData: false,
                    StartValue: 0m,
                    EndValue: 0m,
                    NetContributions: 0m,
                    PnL: 0m,
                    TotalReturn: 0m,
                    BestDayChange: 0m,
                    WorstDayChange: 0m,
                    MaxValue: 0m,
                    MinValue: 0m,
                    MaxDrawdown: 0m,
                    MaxDrawdownStart: DateOnly.MinValue,
                    MaxDrawdownEnd: DateOnly.MinValue,
                    Days: 0
                );
            }
            else
            {

                var firstPoint = points.First();
                var lastPoint = points.Last();

                var startValue = firstPoint.TotalValue;
                var endValue = lastPoint.TotalValue;
                var netContributions = lastPoint.CumulativeNetFlow;
                var pnL = lastPoint.PnL;
                var totalReturn = lastPoint.Return;
                var bestDayChange = points.Max(p => p.PercentChange);
                var worstDayChange = points.Min(p => p.PercentChange);
                var days = points.Count;

                var maxValue = points.Max(p => p.TotalValue);
                var minValue = points.Min(p => p.TotalValue);

                // Calculate drawdowns
                var maxDrawdown = 0m;
                var maxDDStart = firstPoint.Date;
                var maxDDEnd = firstPoint.Date;
                var peak = firstPoint.TotalValue;
                var peakDate = firstPoint.Date;

                foreach (var point in points)
                {
                    if (point.TotalValue > peak)
                    {
                        peak = point.TotalValue;
                        peakDate = point.Date;
                    }

                    if (peak <= 0)
                    {
                        continue;
                    }

                    var drawdown = (point.TotalValue - peak) / peak; // <= 0

                    if (drawdown < maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                        maxDDStart = peakDate;
                        maxDDEnd = point.Date;
                    }
                }

                return new ValuationSummary(
                    PortfolioId: portfolioId,
                    BaseCurrency: baseCurrency,
                    HasData: true,
                    StartValue: startValue,
                    EndValue: endValue,
                    NetContributions: netContributions,
                    PnL: pnL,
                    TotalReturn: totalReturn,
                    BestDayChange: bestDayChange,
                    WorstDayChange: worstDayChange,
                    MaxValue: maxValue,
                    MinValue: minValue,
                    MaxDrawdown: maxDrawdown,
                    MaxDrawdownStart: maxDDStart,
                    MaxDrawdownEnd: maxDDEnd,
                    Days: days
                );
            }
        }

        public static decimal MathRound(decimal value, int decimals = 3, MidpointRounding mode = MidpointRounding.AwayFromZero)
        {
            return Math.Round(value, decimals, mode); // rounding half up
        }
    }

    public record ValuationPoint(
        DateOnly Date,
        decimal TotalValue,
        decimal AbsoluteChange,
        decimal PercentChange,
        decimal NetFlow,
        decimal CumulativeNetFlow,
        decimal PnL,
        decimal Return);

    public record ValuationSummary(
        int PortfolioId,
        string? BaseCurrency,
        bool HasData,
        decimal StartValue,
        decimal EndValue,
        decimal NetContributions,
        decimal PnL,
        decimal TotalReturn,
        decimal BestDayChange,
        decimal WorstDayChange,
        decimal MaxValue,
        decimal MinValue,
        decimal MaxDrawdown,
        DateOnly MaxDrawdownStart,
        DateOnly MaxDrawdownEnd,
        int Days);
}
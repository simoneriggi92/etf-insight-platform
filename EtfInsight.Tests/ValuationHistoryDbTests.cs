using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EtfInsight.Api;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace EtfInsight.Tests
{
    [Collection(nameof(PostgresFixture.DbCollection))]
    public sealed class ValuationHistoryDbTests
    {
        private readonly PostgresFixture _fixture;
        public ValuationHistoryDbTests(PostgresFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_Returns_3Days_20_21_22_WithExpectedMetrics()
        {
            // Arrange the schema and seed data in the test database

            await using (var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                                    drop table if exists portfolio_valuation;
                                    drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio (
                                        id serial primary key,
                                        name text not null
                                    );");
                await Exec(conn, @"create table portfolio_transaction (
                                        portfolio_id int not null references portfolio(id),
                                        trade_date timestamp without time zone not null,
                                        trade_type text not null,
                                        total_amount numeric not null
                                    );");

                await Exec(conn, @"create table portfolio_valuation (
                                        portfolio_id int not null references portfolio(id),
                                        base_currency text,
                                        valuation_date timestamp without time zone not null,
                                        total_value numeric not null
                                    );");

                await Exec(conn, @"insert into portfolio (id, name) values (1, 'Test');");

                await Exec(conn, @"insert into portfolio_transaction (portfolio_id, trade_date, trade_type, total_amount) values
                                    (1, '2025-11-20 00:00:00', 'BUY', 1000)");

                await Exec(conn, @"insert into portfolio_valuation (portfolio_id, base_currency, valuation_date, total_value) values
                                    (1, 'EUR', '2025-11-20 00:00:00', 1000),
                                    (1, 'EUR', '2025-11-21 00:00:00', 1100),
                                    (1, 'EUR', '2025-11-22 00:00:00', 1050);");

                var factory = new TestConnFactory(_fixture.ConnectionString);

                // Act
                var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                    1,
                    from: new DateTime(2025, 11, 20),
                    to: new DateTime(2025, 11, 22),
                    dbConnectionFactory: factory);

                // Assert    
                baseCurrency.Should().Be("EUR");
                points.Should().HaveCount(3);

                points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
                points[0].TotalValue.Should().Be(1000m);
                points[0].NetFlow.Should().Be(1000m);
                points[0].CumulativeNetFlow.Should().Be(1000m);
                points[0].PnL.Should().Be(0m);
                points[0].Return.Should().Be(0m);
                points[0].PercentChange.Should().Be(0m);

                points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
                points[1].TotalValue.Should().Be(1100m);
                points[1].NetFlow.Should().Be(0m);
                points[1].CumulativeNetFlow.Should().Be(1000m);
                points[1].PnL.Should().Be(100m);
                points[1].Return.Should().Be(0.1m);        // Round(3)
                points[1].PercentChange.Should().Be(0.1m); // Round(3)

                points[2].Date.Should().Be(new DateOnly(2025, 11, 22));
                points[2].TotalValue.Should().Be(1050m);
                points[2].NetFlow.Should().Be(0m);
                points[2].CumulativeNetFlow.Should().Be(1000m);
                points[2].PnL.Should().Be(50m);
                points[2].Return.Should().Be(0.05m);
                points[2].PercentChange.Should().Be(-0.045m); // -50/1100 = -0.04545.. -> Round(3) = -0.045        
            }
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_WithSell_ComputesNegativeNetFlow_AndCorrectCumulative()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // BUY day 20
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-20 00:00:00', 'BUY', 1000);");

                // SELL day 22
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-22 00:00:00', 'SELL', 200);");

                // Valuations 20-21-22 (deterministiche)
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1100),
                           (1,'EUR','2025-11-22 00:00:00', 950);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 22),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(3);

            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].TotalValue.Should().Be(1000m);
            points[0].AbsoluteChange.Should().Be(0m);
            points[0].PercentChange.Should().Be(0m);
            points[0].NetFlow.Should().Be(1000m);
            points[0].CumulativeNetFlow.Should().Be(1000m);
            points[0].PnL.Should().Be(0m);
            points[0].Return.Should().Be(0m);

            points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
            points[1].TotalValue.Should().Be(1100m);
            points[1].AbsoluteChange.Should().Be(100m);
            points[1].PercentChange.Should().Be(0.1m);
            points[1].NetFlow.Should().Be(0m);
            points[1].CumulativeNetFlow.Should().Be(1000m);
            points[1].PnL.Should().Be(100m);
            points[1].Return.Should().Be(0.1m);

            points[2].Date.Should().Be(new DateOnly(2025, 11, 22));
            points[2].TotalValue.Should().Be(950m);
            points[2].AbsoluteChange.Should().Be(-150m);
            points[2].PercentChange.Should().Be(-0.136m); // rounded to 3 decimals
            points[2].NetFlow.Should().Be(-200m);          //SELL must become negative net flow
            points[2].CumulativeNetFlow.Should().Be(800m);
            points[2].PnL.Should().Be(150m);
            points[2].Return.Should().Be(0.188m);          // rounded to 3 decimals
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_WithTwoBuys_ComputesCumulativeAndDailyNetFlowCorrectly()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // BUY day 20: +1000
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-20 00:00:00', 'BUY', 1000);");

                // BUY day 21: +500
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-21 00:00:00', 'BUY', 500);");

                // Valuations 20-21-22 (TotalValue series: 1000 -> 1600 -> 1700)
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1600),
                           (1,'EUR','2025-11-22 00:00:00',1700);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 22),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(3);

            // Day 20
            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].TotalValue.Should().Be(1000m);
            points[0].AbsoluteChange.Should().Be(0m);
            points[0].PercentChange.Should().Be(0m);
            points[0].NetFlow.Should().Be(1000m);
            points[0].CumulativeNetFlow.Should().Be(1000m);
            points[0].PnL.Should().Be(0m);
            points[0].Return.Should().Be(0m);

            // Day 21
            points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
            points[1].TotalValue.Should().Be(1600m);
            points[1].AbsoluteChange.Should().Be(600m);
            points[1].PercentChange.Should().Be(0.6m);
            points[1].NetFlow.Should().Be(500m);
            points[1].CumulativeNetFlow.Should().Be(1500m);
            points[1].PnL.Should().Be(100m);
            points[1].Return.Should().Be(0.067m);

            // Day 22
            points[2].Date.Should().Be(new DateOnly(2025, 11, 22));
            points[2].TotalValue.Should().Be(1700m);
            points[2].AbsoluteChange.Should().Be(100m);
            points[2].PercentChange.Should().Be(0.063m);
            points[2].NetFlow.Should().Be(0m);
            points[2].CumulativeNetFlow.Should().Be(1500m);
            points[2].PnL.Should().Be(200m);
            points[2].Return.Should().Be(0.133m);
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_WhenTransactionDayHasNoValuation_CarriesFlowToNextValuation_AndNetFlowTodayIsZero()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null, 
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // BUY day 20: +1000
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-20 00:00:00', 'BUY', 1000);");

                // BUY day 21: +500 (NOTE: there will be NO valuation row for 2025-11-21)
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-21 00:00:00', 'BUY', 500);");

                // Valuations ONLY for 20 and 22 (missing 21)
                // TotalValue series: 1000 -> 1700
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-22 00:00:00',1700);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 22),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(2);

            // Day 20
            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].TotalValue.Should().Be(1000m);
            points[0].AbsoluteChange.Should().Be(0m);
            points[0].PercentChange.Should().Be(0m);
            points[0].NetFlow.Should().Be(1000m);
            points[0].CumulativeNetFlow.Should().Be(1000m);
            points[0].PnL.Should().Be(0m);
            points[0].Return.Should().Be(0m);

            // Day 22
            // The transaction on 21 must be reflected in cumulative, but NOT in NetFlowToday
            points[1].Date.Should().Be(new DateOnly(2025, 11, 22));
            points[1].TotalValue.Should().Be(1700m);
            points[1].AbsoluteChange.Should().Be(700m);
            points[1].PercentChange.Should().Be(0.7m);
            points[1].NetFlow.Should().Be(0m);
            points[1].CumulativeNetFlow.Should().Be(1500m);
            points[1].PnL.Should().Be(200m);
            points[1].Return.Should().Be(0.133m); // 200/1500 = 0.13333.. -> AwayFromZero Round(3) => 0.133 because the 4th decimal is 3
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_TwoTransactionsSameDayDifferentTimes_SumsNetFlowForThatDay()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // Two BUY on the SAME day, different times: +700 and +300 => total +1000 for 2025-11-20
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount) values
                           (1, '2025-11-20 10:00:00', 'BUY', 700),
                           (1, '2025-11-20 15:00:00', 'BUY', 300);");

                // Valuations 20-21
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1100);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 21),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(2);

            // Day 20: NetFlow must be 700+300 = 1000
            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].NetFlow.Should().Be(1000m);
            points[0].CumulativeNetFlow.Should().Be(1000m);
            points[0].PnL.Should().Be(0m);
            points[0].Return.Should().Be(0m);

            // Day 21: No new flows
            points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
            points[1].NetFlow.Should().Be(0m);
            points[1].CumulativeNetFlow.Should().Be(1000m);
            points[1].PnL.Should().Be(100m);
            points[1].Return.Should().Be(0.1m);
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_TransactionAfterToDate_DoesNotAffectCumulative()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // BUY in range
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-20 00:00:00', 'BUY', 1000);");

                // BUY OUT of range (after 'to'): must not be counted
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount)
                           values (1, '2025-11-23 00:00:00', 'BUY', 999);");

                // Valuations 20-21-22
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1100),
                           (1,'EUR','2025-11-22 00:00:00',1050);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 22),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(3);

            // By day 22 cumulative must still be only 1000 (the 999 on day 23 must not be included)
            points[2].Date.Should().Be(new DateOnly(2025, 11, 22));
            points[2].CumulativeNetFlow.Should().Be(1000m);
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_NoTransactions_ReturnIsZero_AndPnLEqualsTotalValue()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // NO transactions inserted

                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1100);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 21),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(2);

            // Day 20: cumulative is 0, so Return must be 0 by definition in your code
            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].NetFlow.Should().Be(0m);
            points[0].CumulativeNetFlow.Should().Be(0m);
            points[0].PnL.Should().Be(1000m);
            points[0].Return.Should().Be(0m);

            // Day 21
            points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
            points[1].NetFlow.Should().Be(0m);
            points[1].CumulativeNetFlow.Should().Be(0m);
            points[1].PnL.Should().Be(1100m);
            points[1].Return.Should().Be(0m);
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_BuyAndSellSameDay_SumsAlgebraicallyIntoSingleNetFlowDay()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // Same day, different times: BUY 1000, SELL 200 => NetFlow for day = 800
                await Exec(conn, @"insert into portfolio_transaction(portfolio_id, trade_date, trade_type, total_amount) values
                           (1, '2025-11-20 10:00:00', 'BUY', 1000),
                           (1, '2025-11-20 15:00:00', 'SELL', 200);");

                // Valuations 20-21
                await Exec(conn, @"insert into portfolio_valuation(portfolio_id, base_currency, valuation_date, total_value) values
                           (1,'EUR','2025-11-20 00:00:00',1000),
                           (1,'EUR','2025-11-21 00:00:00',1100);");
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 21),
                dbConnectionFactory: factory);

            baseCurrency.Should().Be("EUR");
            points.Should().HaveCount(2);

            // Day 20
            points[0].Date.Should().Be(new DateOnly(2025, 11, 20));
            points[0].TotalValue.Should().Be(1000m);
            points[0].NetFlow.Should().Be(800m);
            points[0].CumulativeNetFlow.Should().Be(800m);
            points[0].PnL.Should().Be(200m);
            points[0].Return.Should().Be(0.25m);
            points[0].AbsoluteChange.Should().Be(0m);
            points[0].PercentChange.Should().Be(0m);

            // Day 21
            points[1].Date.Should().Be(new DateOnly(2025, 11, 21));
            points[1].TotalValue.Should().Be(1100m);
            points[1].NetFlow.Should().Be(0m);
            points[1].CumulativeNetFlow.Should().Be(800m);
            points[1].PnL.Should().Be(300m);
            points[1].Return.Should().Be(0.375m);
            points[1].AbsoluteChange.Should().Be(100m);
            points[1].PercentChange.Should().Be(0.1m);
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_NoValuations_AndToNull_ReturnsNullCurrencyAndEmptyPoints()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // NO valuations inserted
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: null,
                to: null,
                dbConnectionFactory: factory);

            baseCurrency.Should().BeNull();
            points.Should().NotBeNull();
            points.Should().BeEmpty();
        }

        [Fact]
        public async Task LoadValuationHistoryAsync_NoValuations_ButToProvided_ReturnsEmptyCurrencyAndEmptyPoints()
        {
            await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conn.OpenAsync();

                await Exec(conn, @"drop table if exists portfolio_transaction;
                           drop table if exists portfolio_valuation;
                           drop table if exists portfolio;");

                await Exec(conn, @"create table portfolio(
                            id int primary key,
                            name text not null
                          );");

                await Exec(conn, @"create table portfolio_transaction(
                            portfolio_id int not null,
                            trade_date timestamp without time zone not null,
                            trade_type text not null,
                            total_amount numeric not null
                          );");

                await Exec(conn, @"create table portfolio_valuation(
                            portfolio_id int not null,
                            base_currency text,
                            valuation_date timestamp without time zone not null,
                            total_value numeric not null
                          );");

                await Exec(conn, "insert into portfolio(id,name) values (1,'Test');");

                // NO valuations inserted
            }

            var factory = new TestConnFactory(_fixture.ConnectionString);

            var (baseCurrency, points) = await ValuationSummaryCalculator.LoadValuationHistoryAsync(
                portfolioId: 1,
                from: new DateTime(2025, 11, 20),
                to: new DateTime(2025, 11, 22),
                dbConnectionFactory: factory);

            baseCurrency.Should().BeNull(); // coerente col tuo codice: baseCurrency = string.Empty e non viene mai valorizzata
            points.Should().NotBeNull();
            points.Should().BeEmpty();
        }

        private static async Task Exec(Npgsql.NpgsqlConnection conn, string sql)
        {
            await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private sealed class TestConnFactory : IDbConnectionFactory
        {
            private readonly string _connectionString;
            public TestConnFactory(string connectionString) => _connectionString = connectionString;
            public NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);
        }
    }
}
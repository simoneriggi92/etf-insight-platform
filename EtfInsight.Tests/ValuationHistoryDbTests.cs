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
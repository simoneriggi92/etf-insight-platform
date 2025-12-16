using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace EtfInsight.Tests
{
    public sealed class PostgresFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgresContainer;

        public string ConnectionString => _postgresContainer.GetConnectionString();

        public PostgresFixture()
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("etfinsight_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .Build();
        }

        public async Task InitializeAsync()
        {
            await _postgresContainer.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _postgresContainer.DisposeAsync();
        }

        [CollectionDefinition(nameof(DbCollection))]
        public sealed class DbCollection : ICollectionFixture<PostgresFixture>
        {
        }

    }
}
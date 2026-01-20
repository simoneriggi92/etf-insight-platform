using System.Data;
using Npgsql;

namespace EtfInsight.Api.IntegrationTests;

public class DatabaseFixture : IDisposable
{
    public string ConnectionString { get; }

    public DatabaseFixture()
    {
        // Test database connection string
        ConnectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=etfinsight;Username=etfinsight;Password=devpassword123;";
    }

    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(ConnectionString);
    }

    public async Task CleanupAsync()
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE TABLE transactions CASCADE;
            TRUNCATE TABLE portfolios CASCADE;
            TRUNCATE TABLE etf_prices CASCADE;
            TRUNCATE TABLE fx_rates CASCADE;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

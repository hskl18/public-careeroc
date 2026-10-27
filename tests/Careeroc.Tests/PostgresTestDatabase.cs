using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Careeroc.Tests;

// Runs a test only when CAREEROC_TEST_POSTGRES points at a server; CI provides one, local runs skip.
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "CAREEROC_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Set {ConnectionStringVariable} to run this test against PostgreSQL.";
        }
    }
}

// A throwaway database with every migration applied, dropped on dispose.
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;
    private readonly string _databaseName;

    private PostgresTestDatabase(string serverConnectionString, string databaseName)
    {
        _serverConnectionString = serverConnectionString;
        _databaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)
            ?? throw new InvalidOperationException($"{PostgresFactAttribute.ConnectionStringVariable} is not set.");
        var database = new PostgresTestDatabase(serverConnectionString, $"careeroc_test_{Guid.NewGuid():N}");
        await database.ExecuteOnServerAsync($"CREATE DATABASE \"{database._databaseName}\"");

        await using var dbContext = database.CreateContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    public CareerocDbContext CreateContext()
    {
        return new CareerocDbContext(
            new DbContextOptionsBuilder<CareerocDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

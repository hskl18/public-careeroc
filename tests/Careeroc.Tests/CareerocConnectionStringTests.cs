using Careeroc.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Careeroc.Tests;

public sealed class CareerocConnectionStringTests
{
    [Fact]
    public void Resolve_UsesThePlatformDatabaseUrlWhenNoConnectionStringIsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://demo:secret@postgres.railway.internal:5432/railway"
            })
            .Build();

        var connectionString = CareerocConnectionString.Resolve(configuration);

        Assert.Contains("Host=postgres.railway.internal", connectionString, StringComparison.Ordinal);
        Assert.Contains("Database=railway", connectionString, StringComparison.Ordinal);
    }
}

using Careeroc.Infrastructure;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Careeroc.Tests;

public sealed class InfrastructureOptionsTests
{
    [Fact]
    public void AddInfrastructureServices_DoesNotEnableOllama_WhenOnlyBaseUrlIsConfigured()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://localhost:11434"
            });
        var services = new ServiceCollection();

        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OllamaEmailExtractionOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Equal("http://localhost:11434", options.BaseUrl);
    }

    [Fact]
    public void AddInfrastructureServices_EnablesOllama_WhenExplicitlyConfigured()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["Ollama:Enabled"] = "true",
                ["Ollama:BaseUrl"] = "http://localhost:11434"
            });
        var services = new ServiceCollection();

        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OllamaEmailExtractionOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal("http://localhost:11434", options.BaseUrl);
    }

    [Fact]
    public void AddInfrastructureServices_PrefersCanonicalOllamaBaseUrlOverLegacyAlias()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "https://canonical.example",
                ["ollama_api"] = "https://legacy.example"
            });
        var services = new ServiceCollection();

        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OllamaEmailExtractionOptions>>().Value;

        Assert.Equal("https://canonical.example", options.BaseUrl);
    }

    [Fact]
    public void AddInfrastructureServices_IgnoresLegacyOllamaModelAliases()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["OLLAMA_MODEL"] = "legacy-model"
            });
        var services = new ServiceCollection();

        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OllamaEmailExtractionOptions>>().Value;

        Assert.Equal("gemma4:31b", options.Model);
    }

    [Fact]
    public void CareerocConnectionString_ConvertsPostgresDatabaseUrl()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgresql://railway:p%40ss@host.railway.internal:5432/railway?sslmode=require"
            });

        var connectionString = CareerocConnectionString.Resolve(configuration);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("host.railway.internal", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("railway", builder.Database);
        Assert.Equal("railway", builder.Username);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void ConfiguredInboxAccountTokenProtector_RoundTripsAndMarksProtectedTokens()
    {
        var protector = new ConfiguredInboxAccountTokenProtector(
            BuildConfiguration(
                new Dictionary<string, string?>
                {
                    ["Security:TokenProtectionKey"] = "test-token-protection-key-that-is-long-enough"
                }));

        var protectedToken = protector.Protect("refresh-token");

        Assert.NotEqual("refresh-token", protectedToken);
        Assert.NotNull(protectedToken);
        Assert.True(ConfiguredInboxAccountTokenProtector.IsProtected(protectedToken!));
        Assert.Equal("refresh-token", protector.Unprotect(protectedToken));
    }

    [Fact]
    public void ConfiguredInboxAccountTokenProtector_DoesNotFallBackToApiOrAuthSecrets()
    {
        var protector = new ConfiguredInboxAccountTokenProtector(
            BuildConfiguration(
                new Dictionary<string, string?>
                {
                    ["Careeroc:ApiAuthSecret"] = "api-secret-should-not-encrypt-inbox-tokens",
                    ["AUTH_SECRET"] = "auth-secret-should-not-encrypt-inbox-tokens"
                }));

        var protectedToken = protector.Protect("refresh-token");

        Assert.Equal("refresh-token", protectedToken);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}

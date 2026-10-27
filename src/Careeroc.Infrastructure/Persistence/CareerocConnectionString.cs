using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Careeroc.Infrastructure.Persistence;

public static class CareerocConnectionString
{
    public const string DefaultLocal =
        "Host=localhost;Port=5432;Database=careeroc;Username=postgres;Password=postgres";

    public static string Resolve(IConfiguration configuration)
    {
        return Normalize(
                configuration.GetConnectionString("Careeroc") ??
                configuration["CAREEROC_CONNECTION_STRING"] ??
                configuration["DATABASE_URL"] ??
                configuration["POSTGRES_URL"] ??
                configuration["POSTGRESQL_URL"])
            ?? DefaultLocal;
    }

    public static string ResolveFromEnvironment()
    {
        return Normalize(
                Environment.GetEnvironmentVariable("ConnectionStrings__Careeroc") ??
                Environment.GetEnvironmentVariable("CAREEROC_CONNECTION_STRING") ??
                Environment.GetEnvironmentVariable("DATABASE_URL") ??
                Environment.GetEnvironmentVariable("POSTGRES_URL") ??
                Environment.GetEnvironmentVariable("POSTGRESQL_URL"))
            ?? DefaultLocal;
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql"))
        {
            return trimmed;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
            if (userInfo.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(userInfo[1]);
            }
        }

        ApplyQueryOptions(builder, uri.Query);
        return builder.ConnectionString;
    }

    private static void ApplyQueryOptions(NpgsqlConnectionStringBuilder builder, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]).Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : string.Empty;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }
    }
}

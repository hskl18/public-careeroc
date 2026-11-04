using System.Text;

namespace Careeroc.Infrastructure.Configuration;

public static class EnvironmentFileLoader
{
    public static void LoadRootEnvFile()
    {
        if (!ShouldLoadRootEnvFile())
        {
            return;
        }

        var envPath = FindRootEnvFile();
        if (envPath is null || !File.Exists(envPath))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(envPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            Environment.SetEnvironmentVariable(key, Unquote(value));
        }
    }

    private static string? FindRootEnvFile()
    {
        var directories = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var startDirectory in directories)
        {
            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static bool ShouldLoadRootEnvFile()
    {
        var explicitOptIn = Environment.GetEnvironmentVariable("CAREEROC_LOAD_ROOT_ENV_FILE");
        if (string.Equals(explicitOptIn, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(explicitOptIn, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var environmentName =
            FirstNonEmpty(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }
}

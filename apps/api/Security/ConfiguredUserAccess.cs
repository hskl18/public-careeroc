using Careeroc.Domain;
using Careeroc.Domain.Entities;

namespace Careeroc.Api.Security;

internal sealed class ConfiguredUserAccess
{
    private readonly IConfiguration _configuration;

    public ConfiguredUserAccess(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public UserAccessSettings GetSettings()
    {
        return UserAccessSettings.FromConfiguration(_configuration);
    }

    public bool IsAllowed(string email)
    {
        return GetSettings().IsAllowed(email);
    }

}

internal sealed record UserAccessSettings(
    IReadOnlySet<string> AllowedEmails,
    IReadOnlySet<string> AllowedDomains)
{
    public bool HasAllowlist => AllowedEmails.Count > 0 || AllowedDomains.Count > 0;

    public bool IsAllowed(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        if (!HasAllowlist)
        {
            return true;
        }

        return AllowedEmails.Contains(normalizedEmail) ||
               AllowedDomains.Contains(ExtractDomain(normalizedEmail));
    }

    public static UserAccessSettings FromConfiguration(IConfiguration configuration)
    {
        return new UserAccessSettings(
            ReadEmailSet(configuration, "Access:AllowedEmails", "ACCESS_ALLOWED_EMAILS"),
            ReadDomainSet(configuration, "Access:AllowedDomains", "ACCESS_ALLOWED_DOMAINS"));
    }

    private static HashSet<string> ReadEmailSet(
        IConfiguration configuration,
        string sectionKey,
        string environmentKey)
    {
        return ReadValues(configuration, sectionKey, environmentKey)
            .Select(NormalizeEmail)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Contains('@', StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ReadDomainSet(
        IConfiguration configuration,
        string sectionKey,
        string environmentKey)
    {
        return ReadValues(configuration, sectionKey, environmentKey)
            .Select(NormalizeDomain)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadValues(
        IConfiguration configuration,
        string sectionKey,
        string environmentKey)
    {
        foreach (var value in configuration.GetSection(sectionKey).Get<string[]>() ?? [])
        {
            foreach (var item in SplitValues(value))
            {
                yield return item;
            }
        }

        foreach (var item in SplitValues(configuration[sectionKey]))
        {
            yield return item;
        }

        foreach (var item in SplitValues(configuration[environmentKey]))
        {
            yield return item;
        }
    }

    private static IEnumerable<string> SplitValues(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeEmail(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeDomain(string value)
    {
        return value.Trim().TrimStart('@').ToLowerInvariant();
    }

    private static string ExtractDomain(string normalizedEmail)
    {
        var atIndex = normalizedEmail.LastIndexOf('@');
        return atIndex < 0 || atIndex == normalizedEmail.Length - 1
            ? string.Empty
            : normalizedEmail[(atIndex + 1)..];
    }
}

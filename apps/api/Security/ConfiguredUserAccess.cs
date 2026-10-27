using Careeroc.Domain;
using Careeroc.Domain.Entities;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    public bool IsConfiguredAdmin(string email)
    {
        return GetSettings().IsAdmin(email);
    }
}

internal sealed record UserAccessSettings(
    IReadOnlySet<string> AllowedEmails,
    IReadOnlySet<string> AllowedDomains,
    IReadOnlySet<string> AdminEmails)
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

    public bool IsAdmin(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        return !string.IsNullOrWhiteSpace(normalizedEmail) &&
               AdminEmails.Contains(normalizedEmail);
    }

    public static UserAccessSettings FromConfiguration(IConfiguration configuration)
    {
        return new UserAccessSettings(
            ReadEmailSet(configuration, "Access:AllowedEmails", "ACCESS_ALLOWED_EMAILS"),
            ReadDomainSet(configuration, "Access:AllowedDomains", "ACCESS_ALLOWED_DOMAINS"),
            ReadEmailSet(configuration, "Admin:Emails", "ADMIN_EMAILS"));
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

internal static class ConfiguredAdminUserSeeder
{
    public static async Task SeedConfiguredAdminUsersAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ConfiguredUserAccess>().GetSettings();
        if (settings.AdminEmails.Count == 0)
        {
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
        foreach (var email in settings.AdminEmails)
        {
            var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Email == email);
            if (user is null)
            {
                await dbContext.Users.AddAsync(
                    new User
                    {
                        Email = email,
                        Name = BuildName(email),
                        Role = UserRoleNames.Admin
                    });
                continue;
            }

            if (!UserRoleNames.IsAdmin(user.Role))
            {
                user.Role = UserRoleNames.Admin;
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static string BuildName(string email)
    {
        var localPart = email.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(localPart) ? email : localPart;
    }
}

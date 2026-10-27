using System.Net.Mail;

namespace Careeroc.Application;

public static class ApplicationDefaults
{
    public const string DefaultUserEmail = "demo@example.com";
    public const string DefaultUserName = "there";
    public const string GmailInboxProvider = "gmail";
    public const string DemoInboxProvider = "demo";
    public const string DefaultTimeZoneId = "America/Los_Angeles";
    public const string UnknownRole = "Unknown Role";
    public const string UnknownCompany = "Unknown Company";

    // A path on this site. Browsers read "//host", "/\\host", and paths with tabs or newlines in
    // them as another origin, so those are refused.
    public static bool IsLocalPath(string value)
    {
        return value.StartsWith('/') &&
               !value.StartsWith("//", StringComparison.Ordinal) &&
               !value.Contains('\\') &&
               !value.Any(char.IsControl) &&
               Uri.TryCreate(value, UriKind.Relative, out _);
    }

    public static string NormalizeRequiredUserEmail(string? userEmail)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new InvalidOperationException("Authenticated user email is required.");
        }

        var normalized = userEmail.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(normalized, out var parsed) ||
            !string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.User) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Authenticated user email is invalid.");
        }

        return normalized;
    }
}

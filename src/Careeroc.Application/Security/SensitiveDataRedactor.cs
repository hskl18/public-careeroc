using System.Text.RegularExpressions;

namespace Careeroc.Application.Security;

public static partial class SensitiveDataRedactor
{
    private const string RedactedEmail = "[redacted-email]";
    private const string RedactedToken = "[redacted-token]";
    private const string RedactedPrompt = "[redacted-prompt]";

    public static string? Redact(string? value, int maxLength = 1000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var redacted = value.Trim();
        redacted = PromptTraceRegex().Replace(redacted, $"$1{RedactedPrompt}");
        redacted = BearerTokenRegex().Replace(redacted, $"$1 {RedactedToken}");
        redacted = KeyValueTokenRegex().Replace(redacted, $"$1{RedactedToken}");
        redacted = LongTokenRegex().Replace(redacted, RedactedToken);
        redacted = EmailRegex().Replace(redacted, RedactedEmail);

        return redacted.Length <= maxLength ? redacted : redacted[..maxLength];
    }

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?i)\b(Bearer)\s+[A-Za-z0-9._~+/=\-]{16,}")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b((?:access_token|refresh_token|id_token|api_key|apikey|authorization|oauth_code|code_verifier|client_secret)\s*[:=]\s*[""']?)[A-Za-z0-9._~+/=\-]{8,}")]
    private static partial Regex KeyValueTokenRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._~+/=\-]{48,}\b")]
    private static partial Regex LongTokenRegex();

    [GeneratedRegex(@"(?is)\b((?:system|developer|user|assistant|model)?\s*prompt\s*[:=]\s*).{12,}")]
    private static partial Regex PromptTraceRegex();
}

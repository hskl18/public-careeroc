using Careeroc.Application.Security;

namespace Careeroc.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_RemovesEmailsAndTokenShapedValues()
    {
        var value =
            "Recruiter jane.recruiter@example.com returned access_token=ya29.a0AfH6SMB123456789 and Authorization: secret-value-123456.";

        var redacted = SensitiveDataRedactor.Redact(value);

        Assert.DoesNotContain("jane.recruiter@example.com", redacted);
        Assert.DoesNotContain("ya29.a0AfH6SMB123456789", redacted);
        Assert.Contains("[redacted-email]", redacted);
        Assert.Contains("[redacted-token]", redacted);
    }

    [Fact]
    public void Redact_RemovesBearerTokensAndPromptTraces()
    {
        var value =
            "Bearer abcdefghijklmnopqrstuvwxyz1234567890. Prompt: raw email body with recruiter details and model instructions.";

        var redacted = SensitiveDataRedactor.Redact(value);

        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz1234567890", redacted);
        Assert.DoesNotContain("raw email body", redacted);
        Assert.Contains("[redacted-token]", redacted);
        Assert.Contains("[redacted-prompt]", redacted);
    }

    [Fact]
    public void Redact_BoundsLongValues()
    {
        var redacted = SensitiveDataRedactor.Redact(string.Concat(Enumerable.Repeat("ordinary text ", 120)), maxLength: 80);

        Assert.NotNull(redacted);
        Assert.Equal(80, redacted!.Length);
    }
}

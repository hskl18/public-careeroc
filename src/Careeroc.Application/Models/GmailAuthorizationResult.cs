namespace Careeroc.Application.Models;

public sealed record GmailAuthorizationResult(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc);

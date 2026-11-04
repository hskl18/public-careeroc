namespace Careeroc.Application.Models;

public sealed record GmailOAuthCompletionResult(
    string ReturnUrl,
    bool Succeeded,
    string Message);

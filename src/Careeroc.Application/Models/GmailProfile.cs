namespace Careeroc.Application.Models;

public sealed record GmailProfile(
    string EmailAddress,
    string? HistoryId = null,
    int? MessagesTotal = null);

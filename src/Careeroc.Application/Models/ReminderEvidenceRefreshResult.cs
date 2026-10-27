namespace Careeroc.Application.Models;

public sealed record ReminderEvidenceRefreshResult(
    int RemindersRefreshed,
    int EmailsUpdated);

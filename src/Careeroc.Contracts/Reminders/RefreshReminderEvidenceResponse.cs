namespace Careeroc.Contracts.Reminders;

public sealed record RefreshReminderEvidenceResponse(
    int RemindersRefreshed,
    int EmailsUpdated);

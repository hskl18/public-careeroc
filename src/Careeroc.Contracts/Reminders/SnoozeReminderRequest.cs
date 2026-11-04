namespace Careeroc.Contracts.Reminders;

public sealed class SnoozeReminderRequest
{
    public DateTimeOffset Until { get; init; }
}

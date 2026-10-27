using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Reminders;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ReminderService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public ReminderService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid reminderId,
        UpdateReminderStatusRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return false;
        }

        var reminder = await _dbContext.Reminders
            .Include(candidate => candidate.JobApplication)
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == reminderId &&
                    candidate.JobApplication.User.Email == resolvedUserEmail,
                cancellationToken);

        if (reminder is null)
        {
            return false;
        }

        var nextStatus = ParseEnum<ReminderStatus>(request.Status);
        if (reminder.Status == nextStatus)
        {
            return true;
        }

        reminder.Status = nextStatus;
        reminder.JobApplication.LastActivityAtUtc = _clock.UtcNow;

        if (nextStatus is not (ReminderStatus.Completed or ReminderStatus.Dismissed))
        {
            _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
            {
                JobApplicationId = reminder.JobApplicationId,
                EventType = ToEventType(nextStatus),
                EventTimeUtc = _clock.UtcNow,
                ExtractedDataJson = JsonSerializer.Serialize(new
                {
                    request.Status,
                    reminder.Type,
                    reminder.Title
                })
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SnoozeAsync(
        Guid reminderId,
        SnoozeReminderRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return false;
        }

        var untilUtc = request.Until.NormalizeUtc();
        if (untilUtc <= _clock.UtcNow)
        {
            throw new InvalidOperationException("Snooze time must be in the future.");
        }

        var reminder = await _dbContext.Reminders
            .Include(candidate => candidate.JobApplication)
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == reminderId &&
                    candidate.JobApplication.User.Email == resolvedUserEmail,
                cancellationToken);

        if (reminder is null)
        {
            return false;
        }

        reminder.DueAtUtc = untilUtc;
        reminder.Status = ReminderStatus.Snoozed;
        reminder.JobApplication.LastActivityAtUtc = _clock.UtcNow;

        _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
        {
            JobApplicationId = reminder.JobApplicationId,
            EventType = "reminder_snoozed",
            EventTimeUtc = _clock.UtcNow,
            ExtractedDataJson = JsonSerializer.Serialize(new
            {
                until = untilUtc,
                reminder.Type,
                reminder.Title
            })
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? null
            : userEmail.Trim().ToLowerInvariant();
    }

    private static string ToEventType(ReminderStatus status)
    {
        return status switch
        {
            ReminderStatus.Completed => "reminder_completed",
            ReminderStatus.Dismissed => "reminder_dismissed",
            ReminderStatus.Snoozed => "reminder_snoozed",
            _ => "reminder_reopened"
        };
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        var normalized = value
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (Enum.TryParse<TEnum>(normalized, true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unsupported value '{value}' for {typeof(TEnum).Name}.");
    }
}

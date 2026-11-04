using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

// Compact, read-only views of one user's pipeline for external assistants over MCP and A2A.
public sealed class AgentQueryService
{
    private const int MaxApplications = 50;

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public AgentQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<AgentPipelineSummaryDto> GetPipelineSummaryAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var userId = await FindUserIdAsync(userEmail, cancellationToken);
        var applications = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId)
            .ToListAsync(cancellationToken);
        var byStatus = applications
            .GroupBy(application => ResumeMailboxContextBuilder.FormatApplicationStatus(application.Status))
            .OrderByDescending(group => group.Count())
            .ToDictionary(group => group.Key, group => group.Count());
        var recent = applications
            .OrderByDescending(application => application.LastActivityAtUtc)
            .Take(5)
            .Select(ToDto)
            .ToList();

        return new AgentPipelineSummaryDto(
            applications.Count,
            byStatus,
            await GetOpenRemindersAsync(userId, null, 10, cancellationToken),
            recent,
            _clock.UtcNow);
    }

    public async Task<IReadOnlyList<AgentApplicationDto>> ListApplicationsAsync(
        string? userEmail,
        ApplicationStatus? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var userId = await FindUserIdAsync(userEmail, cancellationToken);
        var applications = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId && (status == null || application.Status == status))
            .OrderByDescending(application => application.LastActivityAtUtc)
            .Take(Math.Clamp(limit, 1, MaxApplications))
            .ToListAsync(cancellationToken);

        return applications.Select(ToDto).ToList();
    }

    public async Task<AgentApplicationDetailDto?> GetApplicationAsync(
        string? userEmail,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var userId = await FindUserIdAsync(userEmail, cancellationToken);
        var application = await _dbContext.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == applicationId && candidate.UserId == userId,
                cancellationToken);
        if (application is null)
        {
            return null;
        }

        var timeline = await _dbContext.JobApplicationEvents
            .AsNoTracking()
            .Where(applicationEvent => applicationEvent.JobApplicationId == application.Id)
            .OrderBy(applicationEvent => applicationEvent.EventTimeUtc)
            .Select(
                applicationEvent => new AgentTimelineEventDto(
                    applicationEvent.EventType,
                    applicationEvent.EventTimeUtc,
                    applicationEvent.InboxEmail == null ? null : applicationEvent.InboxEmail.Subject))
            .ToListAsync(cancellationToken);

        return new AgentApplicationDetailDto(
            ToDto(application),
            timeline,
            await GetOpenRemindersAsync(userId, application.Id, 10, cancellationToken));
    }

    private async Task<IReadOnlyList<AgentReminderDto>> GetOpenRemindersAsync(
        Guid userId,
        Guid? applicationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        return await _dbContext.Reminders
            .AsNoTracking()
            .Where(
                reminder =>
                    reminder.JobApplication.UserId == userId &&
                    reminder.Status == ReminderStatus.Open &&
                    (applicationId == null || reminder.JobApplicationId == applicationId))
            .OrderBy(reminder => reminder.DueAtUtc)
            .Take(limit)
            .Select(
                reminder => new AgentReminderDto(
                    reminder.JobApplication.Company,
                    reminder.JobApplication.Role,
                    reminder.Title,
                    reminder.DueAtUtc,
                    reminder.DueAtUtc < now))
            .ToListAsync(cancellationToken);
    }

    private async Task<Guid> FindUserIdAsync(string? userEmail, CancellationToken cancellationToken)
    {
        var email = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        return await _dbContext.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static AgentApplicationDto ToDto(JobApplication application)
    {
        return new AgentApplicationDto(
            application.Id,
            application.Company,
            application.Role,
            ResumeMailboxContextBuilder.FormatApplicationStatus(application.Status),
            application.LastActivityAtUtc);
    }
}

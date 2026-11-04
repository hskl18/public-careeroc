using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Careeroc.Application.Services;

public sealed class UserProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> SupportedTimeZoneIds =
    [
        "Etc/UTC",
        "America/Los_Angeles",
        "America/Denver",
        "America/Chicago",
        "America/New_York",
        "Europe/London",
        "Europe/Berlin",
        "Asia/Shanghai",
        "Asia/Tokyo",
        "Asia/Seoul",
        "Asia/Singapore",
        "Asia/Kolkata"
    ];

    private static readonly HashSet<string> SupportedProfileIcons =
    [
        "inbox-blue",
        "inbox-mint",
        "inbox-rose",
        "inbox-alert",
        "inbox-wink",
        "inbox-soft",
        "inbox-sun",
        "inbox-night",
        "inbox-leaf",
        "resume-blue",
        "resume-card",
        "resume-stack",
        "resume-gold",
        "agent-flag",
        "agent-radar",
        "agent-spark",
        "agent-crown",
        "star-scout"
    ];

    private const string DefaultProfileIcon = "inbox-blue";

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public UserProfileService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<UserProfileDto> GetProfileAsync(
        string? userEmail = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, userName, cancellationToken);

        var profileDefaultsChanged = false;
        if (string.IsNullOrWhiteSpace(user.TimeZoneId))
        {
            user.TimeZoneId = ApplicationDefaults.DefaultTimeZoneId;
            profileDefaultsChanged = true;
        }

        if (string.IsNullOrWhiteSpace(user.ProfileIcon))
        {
            user.ProfileIcon = DefaultProfileIcon;
            profileDefaultsChanged = true;
        }

        if (profileDefaultsChanged)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await MapProfileAsync(user, cancellationToken);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(
        UpdateUserProfileRequest request,
        string? userEmail = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, userName, cancellationToken);

        user.Name = NormalizeRequired(
            request.Name,
            ResolveUserName(userName, user.Email),
            maxLength: 200,
            fieldName: "Name");
        user.TimeZoneId = NormalizeTimeZoneId(request.TimeZoneId);
        user.ProfileIcon = NormalizeProfileIcon(request.ProfileIcon, user.ProfileIcon);
        user.Location = NormalizeOptional(request.Location, maxLength: 160, fieldName: "Location");
        user.TargetRole = NormalizeOptional(request.TargetRole, maxLength: 200, fieldName: "Target role");

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapProfileAsync(user, cancellationToken);
    }

    public async Task<UserProfileDto> DismissWelcomeAsync(
        string? userEmail = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, userName, cancellationToken);

        if (!user.WelcomeDismissedAtUtc.HasValue)
        {
            user.WelcomeDismissedAtUtc = _clock.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await MapProfileAsync(user, cancellationToken);
    }

    private async Task<User> GetOrCreateUserAsync(
        string? userEmail,
        string? userName,
        CancellationToken cancellationToken)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            Email = resolvedUserEmail,
            Name = ResolveUserName(userName, resolvedUserEmail),
            TimeZoneId = ApplicationDefaults.DefaultTimeZoneId,
            ProfileIcon = DefaultProfileIcon
        };

        await _dbContext.Users.AddAsync(user, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<UserProfileDto> MapProfileAsync(User user, CancellationToken cancellationToken)
    {
        var evaluation = await BuildEvaluationAsync(user, cancellationToken);
        var memory = await GetConfirmedMemoryAsync(user.Id, cancellationToken);
        return new UserProfileDto(
            user.Email,
            user.Name,
            string.IsNullOrWhiteSpace(user.TimeZoneId)
                ? ApplicationDefaults.DefaultTimeZoneId
                : user.TimeZoneId,
            string.IsNullOrWhiteSpace(user.ProfileIcon)
                ? DefaultProfileIcon
                : user.ProfileIcon,
            user.Location,
            user.TargetRole,
            user.WelcomeDismissedAtUtc,
            user.CreatedAtUtc,
            evaluation,
            memory);
    }

    private async Task<IReadOnlyList<UserMemoryEntryDto>> GetConfirmedMemoryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == userId &&
                entry.ConfirmationStatus == "confirmed" &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .ThenBy(entry => entry.Kind)
            .Take(8)
            .Select(entry => new UserMemoryEntryDto(
                entry.Id,
                entry.Kind,
                entry.Key,
                entry.Value,
                entry.Summary,
                entry.SourceType,
                entry.SourceId,
                entry.Confidence,
                entry.ConfirmationStatus,
                entry.UpdatedAtUtc,
                entry.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<UserProfileEvaluationDto> BuildEvaluationAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        var applicationStats = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == user.Id)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Active = group.Count(application => application.Status != ApplicationStatus.Rejected && application.Status != ApplicationStatus.Closed),
                Interviewing = group.Count(application => application.Status == ApplicationStatus.Interviewing),
                Offers = group.Count(application => application.Status == ApplicationStatus.Offer),
                LastActivityAtUtc = group.Max(application => (DateTimeOffset?)application.LastActivityAtUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var inboxMetadata = await _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .Select(account => account.OAuthMetadataJson)
            .ToListAsync(cancellationToken);
        var memoryEntries = await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .CountAsync(
                entry =>
                    entry.UserId == user.Id &&
                    entry.ConfirmationStatus == "confirmed" &&
                    (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc),
                cancellationToken);
        var latestResume = await _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(version => version.ResumeDocument.UserId == user.Id)
            .OrderByDescending(version => version.CreatedAtUtc)
            .Select(version => new
            {
                Title = version.ResumeDocument.Title,
                Score = version.Evaluation == null ? (int?)null : version.Evaluation.Score
            })
            .FirstOrDefaultAsync(cancellationToken);
        var resumeVersionCount = await _dbContext.ResumeVersions
            .AsNoTracking()
            .CountAsync(version => version.ResumeDocument.UserId == user.Id, cancellationToken);
        var resumeMailboxSummary = await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.Kind == "orchestrator" &&
                entry.Key == "orchestrator.resume_mailbox_bridge")
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .Select(entry => entry.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        var connectedInboxes = inboxMetadata.Count(IsConnectedInboxMetadata);
        var totalApplications = applicationStats?.Total ?? 0;
        var activeApplications = applicationStats?.Active ?? 0;
        var interviewingApplications = applicationStats?.Interviewing ?? 0;
        var offers = applicationStats?.Offers ?? 0;
        var latestResumeScore = latestResume?.Score;
        var score = 20;
        var strengths = new List<string>();
        var gaps = new List<string>();
        var nextActions = new List<string>();

        if (!string.IsNullOrWhiteSpace(user.TargetRole))
        {
            score += 15;
            strengths.Add($"Target role is set to {user.TargetRole}.");
        }
        else
        {
            gaps.Add("Target role is missing.");
            nextActions.Add("Set a target role so mailbox and resume agents can evaluate fit.");
        }

        if (!string.IsNullOrWhiteSpace(user.Location))
        {
            score += 10;
            strengths.Add($"Location preference is set to {user.Location}.");
        }
        else
        {
            gaps.Add("Location preference is missing.");
            nextActions.Add("Add a location preference or remote-work constraint.");
        }

        if (connectedInboxes > 0)
        {
            score += 20;
            strengths.Add($"{connectedInboxes} Gmail inbox{(connectedInboxes == 1 ? " is" : "es are")} connected.");
        }
        else
        {
            gaps.Add("No connected Gmail inbox is available for live recruiting evidence.");
            nextActions.Add("Connect Gmail so the mailbox agent can build application evidence.");
        }

        if (totalApplications > 0)
        {
            score += Math.Min(15, totalApplications);
            strengths.Add($"{totalApplications} application record{(totalApplications == 1 ? "" : "s")} available for pattern analysis.");
        }
        else
        {
            gaps.Add("No application history has been captured yet.");
            nextActions.Add("Backfill Gmail or add applications so the system can learn from outcomes.");
        }

        if (latestResume is not null)
        {
            score += latestResumeScore switch
            {
                >= 85 => 12,
                >= 70 => 9,
                >= 1 => 5,
                _ => 4
            };
            strengths.Add(latestResumeScore.HasValue
                ? $"Latest resume is scored {latestResumeScore}/100."
                : "Latest resume is available for extraction and review.");

            if (latestResumeScore is < 70)
            {
                gaps.Add("Latest resume needs stronger evidence before scaling applications.");
                nextActions.Add("Use resume review to strengthen the sections tied to your target role.");
            }
        }
        else
        {
            gaps.Add("No resume has been imported yet.");
            nextActions.Add("Upload or paste a resume so evaluation can compare it with mailbox outcomes.");
        }

        if (interviewingApplications > 0 || offers > 0)
        {
            score += 15;
            strengths.Add($"{interviewingApplications + offers} high-signal application outcome{(interviewingApplications + offers == 1 ? "" : "s")} found.");
        }
        else if (activeApplications > 0)
        {
            gaps.Add("Application history exists, but no interview or offer signal is visible yet.");
            nextActions.Add("Use analytics and resume review to identify why active applications are not converting.");
        }

        if (memoryEntries > 0)
        {
            score += Math.Min(5, memoryEntries);
            strengths.Add($"{memoryEntries} confirmed memory item{(memoryEntries == 1 ? "" : "s")} available for personalization.");
        }
        else
        {
            gaps.Add("No confirmed long-term memory exists yet.");
            nextActions.Add("Confirm durable career facts as resume and mailbox agents propose them.");
        }

        score = Math.Clamp(score, 0, 100);

        if (nextActions.Count == 0)
        {
            nextActions.Add("Run resume evaluation against the current application targets.");
        }

        return new UserProfileEvaluationDto(
            score,
            BuildSummary(
                score,
                connectedInboxes,
                totalApplications,
                user.TargetRole,
                latestResumeScore,
                resumeMailboxSummary),
            strengths,
            gaps,
            nextActions.Take(4).ToList(),
            new UserProfileEvaluationEvidenceDto(
                totalApplications,
                activeApplications,
                interviewingApplications,
                offers,
                connectedInboxes,
                inboxMetadata.Count,
                memoryEntries,
                applicationStats?.LastActivityAtUtc,
                resumeVersionCount,
                latestResumeScore,
                latestResume?.Title,
                resumeMailboxSummary));
    }

    private static string BuildSummary(
        int score,
        int connectedInboxes,
        int totalApplications,
        string? targetRole,
        int? latestResumeScore,
        string? resumeMailboxSummary)
    {
        var target = string.IsNullOrWhiteSpace(targetRole)
            ? "a target role"
            : targetRole.Trim();

        if (!string.IsNullOrWhiteSpace(resumeMailboxSummary))
        {
            return resumeMailboxSummary.Trim();
        }

        if (latestResumeScore.HasValue && connectedInboxes > 0 && totalApplications > 0)
        {
            return $"Resume score is {latestResumeScore}/100 with {totalApplications} mailbox-backed application records for {target}.";
        }

        if (score >= 80)
        {
            return $"Strong profile foundation for {target}; enough mailbox evidence exists to support tailored resume and job-search recommendations.";
        }

        if (connectedInboxes > 0 || totalApplications > 0)
        {
            return $"Useful early profile for {target}; the next lift is filling missing context and connecting resume evidence.";
        }

        return $"Profile is still cold for {target}; connect evidence sources before trusting deeper agent recommendations.";
    }

    private static bool IsConnectedInboxMetadata(string? oauthMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(oauthMetadataJson))
        {
            return false;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<InboxAccountMetadata>(
                oauthMetadataJson,
                JsonOptions);
            return metadata?.ConnectionStatus == "Connected";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeTimeZoneId(string? value)
    {
        var timeZoneId = string.IsNullOrWhiteSpace(value)
            ? ApplicationDefaults.DefaultTimeZoneId
            : value.Trim();

        if (!SupportedTimeZoneIds.Contains(timeZoneId))
        {
            throw new InvalidOperationException("Unsupported time zone.");
        }

        return timeZoneId;
    }

    private static string NormalizeProfileIcon(string? value, string? existingValue)
    {
        var profileIcon = string.IsNullOrWhiteSpace(value)
            ? existingValue?.Trim()
            : value.Trim();

        if (string.IsNullOrWhiteSpace(profileIcon))
        {
            return DefaultProfileIcon;
        }

        if (!SupportedProfileIcons.Contains(profileIcon))
        {
            throw new InvalidOperationException($"Unsupported profile icon: {profileIcon}");
        }

        return profileIcon;
    }

    private static string NormalizeRequired(
        string? value,
        string fallback,
        int maxLength,
        string fieldName)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback.Trim()
            : value.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldName} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }

    private static string ResolveUserName(string? userName, string userEmail)
    {
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName.Trim();
        }

        var separatorIndex = userEmail.IndexOf('@');
        return separatorIndex > 0 ? userEmail[..separatorIndex] : ApplicationDefaults.DefaultUserName;
    }
}

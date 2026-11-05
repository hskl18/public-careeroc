using System.Net;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Review;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ManualReviewQueryService
{
    private static readonly Regex HtmlBreakRegex = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new("\\bhttps?://\\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LongTokenRegex = new("\\b[A-Za-z0-9_=.-]{48,}\\b", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    private readonly ICareerocDbContext _dbContext;

    public ManualReviewQueryService(ICareerocDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ManualReviewResponse> GetAsync(
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        var categoryOptions = Enum.GetValues<EmailCategory>()
            .Select(ToSnakeCase)
            .ToArray();

        var statusOptions = Enum.GetValues<ApplicationStatus>()
            .Select(ToLabel)
            .ToArray();

        if (user is null)
        {
            return new ManualReviewResponse([], [], categoryOptions, statusOptions, 0, 0);
        }

        var inboxAccounts = await _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var visibleInboxAccounts = inboxAccounts;
        var visibleInboxAccountIds = visibleInboxAccounts
            .Select(account => account.Id)
            .ToHashSet();

        var linkedEmails = await _dbContext.InboxEmails
            .AsNoTracking()
            .Where(email => email.InboxAccount.UserId == user.Id && email.JobApplicationId.HasValue)
            .Select(email => new { email.InboxAccountId, ApplicationId = email.JobApplicationId!.Value })
            .ToListAsync(cancellationToken);
        var applicationIdsWithAnyLinkedEmail = linkedEmails
            .Select(email => email.ApplicationId)
            .ToHashSet();
        var visibleApplicationIds = linkedEmails
            .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId))
            .Select(email => email.ApplicationId)
            .ToHashSet();

        var applicationEntities = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == user.Id)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .ToListAsync(cancellationToken);
        applicationEntities = applicationEntities
            .Where(
                application =>
                    !applicationIdsWithAnyLinkedEmail.Contains(application.Id) ||
                    visibleApplicationIds.Contains(application.Id))
            .ToList();

        var reviewEmailQuery = _dbContext.InboxEmails
            .AsNoTracking()
            .Where(
                email =>
                    email.InboxAccount.UserId == user.Id &&
                    email.RequiresManualReview &&
                    visibleInboxAccountIds.Contains(email.InboxAccountId));

        var totalWaiting = await reviewEmailQuery.CountAsync(cancellationToken);
        var totalUnassigned = await reviewEmailQuery
            .CountAsync(email => email.JobApplicationId == null, cancellationToken);

        var emailEntities = await reviewEmailQuery
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var threadIds = emailEntities
            .Select(email => email.ThreadId)
            .Where(threadId => !string.IsNullOrWhiteSpace(threadId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var threadEmailEntities = threadIds.Length == 0
            ? new List<Domain.Entities.InboxEmail>()
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(
                    email =>
                        email.InboxAccount.UserId == user.Id &&
                        visibleInboxAccountIds.Contains(email.InboxAccountId) &&
                        threadIds.Contains(email.ThreadId))
                .OrderBy(email => email.ReceivedAtUtc)
                .ToListAsync(cancellationToken);

        var threadMessagesByThreadId = threadEmailEntities
            .GroupBy(email => email.ThreadId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewThreadMessageDto>)group
                    .OrderBy(email => email.ReceivedAtUtc)
                    .TakeLast(6)
                    .Select(
                        email => new ReviewThreadMessageDto(
                            email.Id,
                            email.Sender,
                            email.Subject,
                            BuildSnippet(email.ParsedBody, email.RawBody),
                            email.ReceivedAtUtc,
                            email.Category.HasValue ? ToSnakeCase(email.Category.Value) : null,
                            email.RequiresManualReview,
                            false))
                    .ToList(),
                StringComparer.Ordinal);

        var applications = applicationEntities
            .Select(
                application => new ReviewApplicationDto(
                    application.Id,
                    application.Company,
                    application.Role,
                    ToLabel(application.Status),
                    application.Priority,
                    application.LastActivityAtUtc))
            .ToList();
        var emails = emailEntities
            .Select(
                email =>
                {
                    var agentTrace = BuildAgentTrace(email);
                    var traceSummary = AgentTraceSummaryBuilder.FromInboxEmail(email);

                    return new ReviewEmailDto(
                        email.Id,
                        email.ThreadId,
                        email.Sender,
                        email.Subject,
                        BuildSnippet(email.ParsedBody, email.RawBody),
                        BuildBodyPreview(email.ParsedBody, email.RawBody),
                        email.ReceivedAtUtc,
                        email.ClassificationConfidence,
                        email.MatchingConfidence,
                        email.RequiresManualReview,
                        email.ReviewReason,
                        email.Category.HasValue ? ToSnakeCase(email.Category.Value) : null,
                        email.ExtractedCompany,
                        email.ExtractedRole,
                        email.ContactName,
                        email.ContactEmail,
                        email.ContactType.HasValue ? ToLabel(email.ContactType.Value) : null,
                        email.ContactRoleHint,
                        email.ActionRequired,
                        email.DueDateUtc,
                        email.ProcessingNotes,
                        ExtractProcessingSource(agentTrace),
                        agentTrace,
                        email.JobApplicationId,
                        BuildThreadMessages(
                            email,
                            threadMessagesByThreadId.TryGetValue(email.ThreadId, out var threadMessages)
                                ? threadMessages
                                : []),
                        traceSummary);
                })
            .ToList();

        return new ManualReviewResponse(
            emails,
            applications,
            categoryOptions,
            statusOptions,
            totalWaiting,
            totalUnassigned);
    }

    private static string BuildSnippet(string parsedBody, string rawBody)
    {
        var normalized = BuildReadableEmailExcerpt(parsedBody, rawBody);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No readable email excerpt available.";
        }

        return Truncate(normalized, 180);
    }

    private static string BuildBodyPreview(string parsedBody, string rawBody)
    {
        var normalized = BuildReadableEmailExcerpt(parsedBody, rawBody);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No readable email excerpt available.";
        }

        return Truncate(normalized, 700);
    }

    private static string BuildReadableEmailExcerpt(string parsedBody, string rawBody)
    {
        var source = !string.IsNullOrWhiteSpace(parsedBody) ? parsedBody : rawBody;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(source)
            .Replace('\u00a0', ' ');
        var withoutBreaks = HtmlBreakRegex.Replace(decoded, " ");
        var withoutTags = HtmlTagRegex.Replace(withoutBreaks, " ");
        var withoutUrls = UrlRegex.Replace(withoutTags, " ");
        var withoutTrackingTokens = LongTokenRegex.Replace(withoutUrls, " ");

        return NormalizeReadableText(withoutTrackingTokens);
    }

    private static string NormalizeReadableText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var sliceLength = maxLength - 3;
        var lastSpaceIndex = value.LastIndexOf(' ', sliceLength);
        if (lastSpaceIndex < maxLength / 2)
        {
            lastSpaceIndex = sliceLength;
        }

        return $"{value[..lastSpaceIndex].TrimEnd()}...";
    }

    private static string? ExtractProcessingSource(ReviewAgentTraceDto? agentTrace)
    {
        var trace = agentTrace is null
            ? null
            : new InboxEmailAgentTrace(
                agentTrace.GeneratedAtUtc ?? DateTimeOffset.MinValue,
                agentTrace.Steps.Select(
                    step => new AgentTraceStep(
                        step.Stage,
                        step.AgentName,
                        step.Summary,
                        step.Reason,
                        step.Facts.Select(fact => new AgentTraceFact(fact.Label, fact.Value)).ToArray()))
                .ToArray());
        return InboxEmailAgentTraceJson.ExtractProcessingSource(trace);
    }

    private static ReviewAgentTraceDto? BuildAgentTrace(Domain.Entities.InboxEmail email)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        return trace is null ? null : ToReviewAgentTrace(trace, email);
    }

    private static ReviewAgentTraceDto ToReviewAgentTrace(InboxEmailAgentTrace trace, Domain.Entities.InboxEmail email)
    {
        return new ReviewAgentTraceDto(
            trace.GeneratedAtUtc,
            trace.Steps.Select(
                step => new ReviewAgentTraceStepDto(
                    step.Stage,
                    step.AgentName,
                    step.Summary,
                    step.Reason,
                    step.Facts.Select(fact => new ReviewAgentTraceFactDto(fact.Label, fact.Value)).ToArray()))
            .ToArray(),
            AgentTraceSummaryBuilder.FromInboxEmail(email));
    }

    private static IReadOnlyList<ReviewThreadMessageDto> BuildThreadMessages(
        Domain.Entities.InboxEmail email,
        IReadOnlyList<ReviewThreadMessageDto> threadMessages)
    {
        if (threadMessages.Count == 0)
        {
            return
            [
                new ReviewThreadMessageDto(
                    email.Id,
                    email.Sender,
                    email.Subject,
                    BuildSnippet(email.ParsedBody, email.RawBody),
                    email.ReceivedAtUtc,
                    email.Category.HasValue ? ToSnakeCase(email.Category.Value) : null,
                    email.RequiresManualReview,
                    true)
            ];
        }

        return threadMessages
            .Select(
                message => message with
                {
                    IsCurrentEmail = message.Id == email.Id
                })
            .ToList();
    }

    private static string ToSnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return string.Concat(
            value.ToString().Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $"_{char.ToLowerInvariant(character)}"
                        : char.ToLowerInvariant(character).ToString()));
    }

    private static string ToLabel<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return string.Concat(
            value.ToString().Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $" {character}"
                        : character.ToString()));
    }

    private static string ToLabel(string value)
    {
        return string.Join(
            " ",
            value
                .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }
}

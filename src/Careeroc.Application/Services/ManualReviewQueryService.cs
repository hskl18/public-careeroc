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
            return new ManualReviewResponse([], [], [], categoryOptions, statusOptions, 0, 0, 0);
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
        var visibleApplicationIdsForReview = applicationEntities
            .Select(application => application.Id)
            .ToHashSet();

        var artifactReviewQuery = _dbContext.ApplicationArtifactEvidence
            .AsNoTracking()
            .Where(
                evidence =>
                    evidence.UserId == user.Id &&
                    evidence.RequiresReview &&
                    visibleApplicationIdsForReview.Contains(evidence.JobApplicationId));
        var totalArtifactReviews = await artifactReviewQuery.CountAsync(cancellationToken);
        var artifactEntities = await artifactReviewQuery
            .OrderByDescending(evidence => evidence.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var applicationById = applicationEntities.ToDictionary(application => application.Id);
        var artifacts = artifactEntities
            .Where(evidence => applicationById.ContainsKey(evidence.JobApplicationId))
            .Select(
                evidence =>
                {
                    var application = applicationById[evidence.JobApplicationId];
                    return new ReviewArtifactEvidenceDto(
                        evidence.Id,
                        evidence.JobApplicationId,
                        application.Company,
                        application.Role,
                        evidence.ArtifactKind,
                        evidence.FileName,
                        evidence.SafeTitle,
                        evidence.ExtractedFactType,
                        evidence.ExtractedFactText,
                        evidence.NormalizedDateUtc,
                        evidence.Confidence,
                        evidence.SourceSnippet,
                        evidence.ReviewReason ?? "Artifact evidence needs manual review before it can influence application state or reminders.",
                        evidence.CreatedAtUtc,
                        AgentTraceSummaryBuilder.FromArtifactEvidence(evidence),
                        BuildArtifactAgentTrace(evidence));
                })
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
                        ExtractProcessingSource(email.ProcessingNotes, agentTrace),
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
            artifacts,
            applications,
            categoryOptions,
            statusOptions,
            totalWaiting + totalArtifactReviews,
            totalUnassigned,
            totalArtifactReviews);
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

    private static string? ExtractProcessingSource(
        string? notes,
        ReviewAgentTraceDto? agentTrace)
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
        var traceSource = InboxEmailAgentTraceJson.ExtractProcessingSource(trace);
        if (!string.IsNullOrWhiteSpace(traceSource))
        {
            return traceSource;
        }

        return ExtractLegacyProcessingSource(notes);
    }

    private static string? ExtractLegacyProcessingSource(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || !notes.StartsWith("[", StringComparison.Ordinal))
        {
            return null;
        }

        var closingBracketIndex = notes.IndexOf(']');
        if (closingBracketIndex <= 1)
        {
            return null;
        }

        return notes[1..closingBracketIndex].Trim();
    }

    private static ReviewAgentTraceDto? BuildAgentTrace(Domain.Entities.InboxEmail email)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        if (trace is not null)
        {
            return ToReviewAgentTrace(trace, email);
        }

        var steps = new List<ReviewAgentTraceStepDto>();
        var legacyProcessingSource = ExtractLegacyProcessingSource(email.ProcessingNotes);

        if (!string.IsNullOrWhiteSpace(legacyProcessingSource) ||
            email.Category.HasValue ||
            email.ClassificationConfidence.HasValue ||
            !string.IsNullOrWhiteSpace(email.ExtractedCompany) ||
            !string.IsNullOrWhiteSpace(email.ExtractedRole))
        {
            var extractionFacts = new List<ReviewAgentTraceFactDto>();

            if (email.Category.HasValue)
            {
                extractionFacts.Add(new ReviewAgentTraceFactDto("Category", ToSnakeCase(email.Category.Value)));
            }

            if (!string.IsNullOrWhiteSpace(email.ExtractedCompany))
            {
                extractionFacts.Add(new ReviewAgentTraceFactDto("Company", email.ExtractedCompany));
            }

            if (!string.IsNullOrWhiteSpace(email.ExtractedRole))
            {
                extractionFacts.Add(new ReviewAgentTraceFactDto("Role", email.ExtractedRole));
            }

            if (email.ClassificationConfidence.HasValue)
            {
                extractionFacts.Add(
                    new ReviewAgentTraceFactDto(
                        "Classification confidence",
                        $"{email.ClassificationConfidence.Value:0.00}"));
            }

            extractionFacts.Add(new ReviewAgentTraceFactDto("Action required", email.ActionRequired ? "Yes" : "No"));

            if (email.DueDateUtc.HasValue)
            {
                extractionFacts.Add(
                    new ReviewAgentTraceFactDto(
                        "Due date (UTC)",
                        email.DueDateUtc.Value.ToString("O")));
            }

            steps.Add(
                new ReviewAgentTraceStepDto(
                    "workflow_extraction",
                    legacyProcessingSource ?? "Legacy workflow extraction",
                    email.ProcessingNotes ?? "Structured extraction metadata was recorded before agent tracing was available.",
                    null,
                    extractionFacts));
        }

        if (!string.IsNullOrWhiteSpace(email.ContactEmail) ||
            !string.IsNullOrWhiteSpace(email.ContactName) ||
            email.ContactType.HasValue ||
            !string.IsNullOrWhiteSpace(email.ContactRoleHint))
        {
            var contactFacts = new List<ReviewAgentTraceFactDto>();

            if (!string.IsNullOrWhiteSpace(email.ContactName))
            {
                contactFacts.Add(new ReviewAgentTraceFactDto("Contact name", email.ContactName));
            }

            if (!string.IsNullOrWhiteSpace(email.ContactEmail))
            {
                contactFacts.Add(new ReviewAgentTraceFactDto("Contact email", email.ContactEmail));
            }

            if (email.ContactType.HasValue)
            {
                contactFacts.Add(new ReviewAgentTraceFactDto("Contact type", ToLabel(email.ContactType.Value)));
            }

            if (!string.IsNullOrWhiteSpace(email.ContactRoleHint))
            {
                contactFacts.Add(new ReviewAgentTraceFactDto("Role hint", email.ContactRoleHint));
            }

            steps.Add(
                new ReviewAgentTraceStepDto(
                    "recruiter_identity",
                    "Recruiter Identity Agent",
                    "Captured recruiting contact metadata from the sender and message heuristics.",
                    null,
                    contactFacts));
        }

        if (email.RequiresManualReview || !string.IsNullOrWhiteSpace(email.ReviewReason) || email.MatchingConfidence.HasValue)
        {
            var reviewFacts = new List<ReviewAgentTraceFactDto>
            {
                new("Requires manual review", email.RequiresManualReview ? "Yes" : "No")
            };

            if (email.MatchingConfidence.HasValue)
            {
                reviewFacts.Add(
                    new ReviewAgentTraceFactDto(
                        "Match confidence",
                        $"{email.MatchingConfidence.Value:0.00}"));
            }

            steps.Add(
                new ReviewAgentTraceStepDto(
                    "review_evidence",
                    "Legacy review evidence",
                    email.RequiresManualReview
                        ? "Escalated by the review agent."
                        : "Cleared by the review agent.",
                    email.ReviewReason,
                    reviewFacts));
        }

        return steps.Count == 0
            ? null
            : new ReviewAgentTraceDto(email.ProcessedAtUtc, steps, AgentTraceSummaryBuilder.FromInboxEmail(email));
    }

    private static ReviewAgentTraceDto BuildArtifactAgentTrace(ApplicationArtifactEvidence evidence)
    {
        var summary = AgentTraceSummaryBuilder.FromArtifactEvidence(evidence);

        return new ReviewAgentTraceDto(
            evidence.CreatedAtUtc,
            [
                new ReviewAgentTraceStepDto(
                    "artifact_ingestion",
                    "Artifact evidence importer",
                    $"Accepted {evidence.ContentType} evidence as {ToLabel(evidence.ArtifactKind)}.",
                    "The artifact path stores evidence metadata and does not keep raw uploaded or downloaded bytes.",
                    [
                        new ReviewAgentTraceFactDto("file", evidence.FileName),
                        new ReviewAgentTraceFactDto("source", summary.EvidenceSource)
                    ]),
                new ReviewAgentTraceStepDto(
                    "gemma_extraction",
                    "Artifact extractor",
                    evidence.ExtractedFactText,
                    evidence.SourceSnippet,
                    [
                        new ReviewAgentTraceFactDto("model", summary.ModelPath ?? "not recorded"),
                        new ReviewAgentTraceFactDto("purpose", summary.Purpose),
                        new ReviewAgentTraceFactDto("confidence", $"{Math.Round(evidence.Confidence * 100)}%")
                    ]),
                new ReviewAgentTraceStepDto(
                    "review_gate",
                    "Artifact review gate",
                    evidence.RequiresReview
                        ? "Blocked this artifact from mutating application state."
                        : "Recorded evidence without mutating application state.",
                    evidence.ReviewReason ??
                    "Artifact evidence remains an auditable card; application status and reminders require a separate review path.",
                    [
                        new ReviewAgentTraceFactDto("outcome", summary.ReviewGateResult),
                        new ReviewAgentTraceFactDto("fallback", summary.FallbackPath)
                    ])
            ],
            summary);
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

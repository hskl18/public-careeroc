using System.Globalization;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Dashboard;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

// Row shapes the dashboard query reads from the database.
public sealed partial class DashboardQueryService
{
    private sealed record DashboardUserRecord(
        Guid Id,
        string Email);

    private sealed record DashboardInboxAccountRecord(
        Guid Id,
        string Provider,
        string EmailAddress,
        string? OAuthMetadataJson,
        DateTimeOffset? LastSyncedAtUtc,
        DateTimeOffset CreatedAtUtc);

    private sealed record DashboardEmailRecord(
        Guid Id,
        Guid InboxAccountId,
        Guid? JobApplicationId,
        string Provider,
        string AccountEmailAddress,
        string ThreadId,
        string ExternalMessageId,
        string Subject,
        string Sender,
        DateTimeOffset ReceivedAtUtc,
        DateTimeOffset? ProcessedAtUtc,
        DateTimeOffset? DueDateUtc,
        EmailCategory? Category,
        bool ActionRequired,
        bool RequiresManualReview,
        string? ReviewReason,
        decimal? ClassificationConfidence,
        bool IsOutbound)
    {
        public string? GmailUrl => InboxEmailLinkBuilder.BuildGmailUrl(
            Provider,
            AccountEmailAddress,
            ThreadId,
            ExternalMessageId);
    }

    private sealed record DashboardApplicationEmailSummaryRecord(
        Guid JobApplicationId,
        DateTimeOffset FirstReceivedAtUtc,
        string? LatestProvider,
        string? LatestAccountEmailAddress,
        string? LatestThreadId,
        string? LatestExternalMessageId)
    {
        public string? GmailUrl => string.IsNullOrWhiteSpace(LatestProvider) ||
            string.IsNullOrWhiteSpace(LatestAccountEmailAddress) ||
            string.IsNullOrWhiteSpace(LatestThreadId) ||
            string.IsNullOrWhiteSpace(LatestExternalMessageId)
                ? null
                : InboxEmailLinkBuilder.BuildGmailUrl(
                    LatestProvider,
                    LatestAccountEmailAddress,
                    LatestThreadId,
                    LatestExternalMessageId);
    }

    private sealed record DashboardAnalysisJobRecord(
        EmailProcessingStatus Status,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? LastError);

    private sealed record DashboardInboxOverviewCounts(
        int SyncedEmails,
        int ProcessedEmails,
        int ReviewQueue);

    private sealed record DashboardPendingActionRecord(
        DashboardReminderRecord Reminder,
        DashboardEmailRecord? EvidenceEmail);

    private sealed record DashboardReminderRecord(
        Guid Id,
        Guid JobApplicationId,
        string Company,
        string Role,
        ReminderType Type,
        string Title,
        DateTimeOffset DueAtUtc,
        ReminderStatus Status);

    private sealed record DashboardEventRecord(
        Guid Id,
        Guid JobApplicationId,
        Guid? InboxEmailId,
        string EventType,
        DateTimeOffset EventTimeUtc,
        string Company,
        string Role,
        DateTimeOffset? InboxEmailReceivedAtUtc);

    private sealed record ApplicationReviewState(
        decimal? ParserConfidence,
        bool RequiresReview,
        string? ReviewReason);

    private sealed record PrioritySummary(
        string Label,
        string Reason);
}

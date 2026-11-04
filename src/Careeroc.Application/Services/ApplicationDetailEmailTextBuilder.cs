using System.Net;
using System.Text.RegularExpressions;
using Careeroc.Application.Models;
using Careeroc.Domain.Enums;

namespace Careeroc.Application.Services;

internal static partial class ApplicationDetailEmailTextBuilder
{
    public static string BuildMessageSummary(
        string? processingTraceJson,
        bool requiresManualReview,
        string? reviewReason,
        bool isOutbound,
        EmailCategory? category,
        string parsedBody,
        string rawBody)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(processingTraceJson);
        var workflowSummary = trace?.Steps.FirstOrDefault(
            step => string.Equals(step.Stage, "workflow_extraction", StringComparison.OrdinalIgnoreCase))?.Summary;
        if (!string.IsNullOrWhiteSpace(workflowSummary))
        {
            return Truncate(HumanizeSystemCopy(NormalizeReadableText(workflowSummary)), 220);
        }

        if (requiresManualReview && !string.IsNullOrWhiteSpace(reviewReason))
        {
            return Truncate($"Needs review: {NormalizeReadableText(reviewReason)}", 220);
        }

        if (isOutbound)
        {
            return "Sent reply found in this thread.";
        }

        return category switch
        {
            EmailCategory.RecruiterOutreach => "Recruiter outreach received from this message.",
            EmailCategory.ApplicationReceived => "Application receipt received from this message.",
            EmailCategory.OaInvitation => "Assessment or questionnaire request detected in this message.",
            EmailCategory.InterviewInvitation => "Interview invitation detected in this message.",
            EmailCategory.InterviewConfirmation => "Interview confirmation received from this message.",
            EmailCategory.Rejection => "Rejection decision received from this message.",
            EmailCategory.Offer => "Offer-stage update received from this message.",
            EmailCategory.GenericUpdate => "General recruiting update received from this message.",
            _ => BuildSnippet(parsedBody, rawBody)
        };
    }

    public static string BuildSnippet(string parsedBody, string rawBody)
    {
        var normalized = BuildReadableEmailExcerpt(parsedBody, rawBody);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No readable email excerpt available.";
        }

        return Truncate(normalized, 180);
    }

    public static string BuildBodyPreview(string parsedBody, string rawBody)
    {
        var normalized = BuildReadableEmailExcerpt(parsedBody, rawBody);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No readable email excerpt available.";
        }

        return Truncate(normalized, 520);
    }

    private static string HumanizeSystemCopy(string value)
    {
        return value
            .Replace("Application receipt logged", "Application receipt received", StringComparison.OrdinalIgnoreCase)
            .Replace("Recruiter outreach captured", "Recruiter outreach received", StringComparison.OrdinalIgnoreCase)
            .Replace("Interview confirmation recorded", "Interview confirmation received", StringComparison.OrdinalIgnoreCase)
            .Replace("General recruiting update logged", "Recruiting update received", StringComparison.OrdinalIgnoreCase)
            .Replace("Rejection recorded", "Rejection received", StringComparison.OrdinalIgnoreCase)
            .Replace("Offer recorded", "Offer received", StringComparison.OrdinalIgnoreCase)
            .Replace("Sent Gmail reply recorded", "Sent Gmail reply found", StringComparison.OrdinalIgnoreCase);
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
        var withoutBreaks = HtmlBreakRegex().Replace(decoded, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutBreaks, " ");
        var withoutUrls = UrlRegex().Replace(withoutTags, " ");
        var withoutTrackingTokens = LongTokenRegex().Replace(withoutUrls, " ");

        return NormalizeReadableText(withoutTrackingTokens);
    }

    private static string NormalizeReadableText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(value, " ").Trim();
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

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreakRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\bhttps?://\\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("\\b[A-Za-z0-9_=.-]{48,}\\b")]
    private static partial Regex LongTokenRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}

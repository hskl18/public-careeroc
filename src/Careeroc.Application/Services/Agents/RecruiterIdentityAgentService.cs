using System.Net.Mail;
using System.Text.RegularExpressions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Application.Services.Agents;

public sealed partial class RecruiterIdentityAgentService
{
    private static readonly HashSet<string> GenericMailboxTokens =
    [
        "careers",
        "hiring",
        "jobs",
        "noreply",
        "notifications",
        "recruiting",
        "talent",
        "team",
        "updates"
    ];

    public RecruiterIdentityDecision Identify(
        InboxEmail inboxEmail,
        ParsedRecruitingEmail parsed)
    {
        var (displayName, emailAddress, localPart) = ParseSender(inboxEmail.Sender);
        var contactName = NormalizeContactName(displayName, localPart);
        var contactType = ResolveContactType(displayName, localPart, inboxEmail, parsed);
        var roleHint = ResolveRoleHint(displayName, localPart, inboxEmail, contactType);
        var confidence = ResolveConfidence(emailAddress, contactName, contactType, roleHint);

        if (string.IsNullOrWhiteSpace(emailAddress) && string.IsNullOrWhiteSpace(contactName))
        {
            return new RecruiterIdentityDecision(
                "Recruiter Identity Agent",
                null,
                null,
                null,
                null,
                0.20m,
                "Could not identify a recruiting contact from the sender metadata.",
                "The sender metadata did not include a usable human name or mailbox.");
        }

        if (contactType.HasValue)
        {
            var subjectName = !string.IsNullOrWhiteSpace(contactName)
                ? contactName
                : emailAddress!;

            return new RecruiterIdentityDecision(
                "Recruiter Identity Agent",
                contactName,
                emailAddress,
                roleHint,
                contactType,
                confidence,
                $"Identified {subjectName} as {ToLabel(contactType.Value)} for this thread.",
                roleHint is null
                    ? "Classified the sender using mailbox and message-role heuristics."
                    : $"Captured the sender as {roleHint} using mailbox and message-role heuristics.");
        }

        return new RecruiterIdentityDecision(
            "Recruiter Identity Agent",
            contactName,
            emailAddress,
            roleHint,
            null,
            confidence,
            "Captured sender contact metadata, but could not classify the recruiting role confidently.",
            "The sender looked relevant, but the mailbox and message content did not clearly indicate recruiter, hiring manager, coordinator, or interviewer ownership.");
    }

    private static (string? DisplayName, string? EmailAddress, string? LocalPart) ParseSender(string sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            return (null, null, null);
        }

        try
        {
            var address = new MailAddress(sender.Trim());
            var emailAddress = address.Address.Trim().ToLowerInvariant();
            var localPart = emailAddress.Split('@').FirstOrDefault();
            var displayName = string.IsNullOrWhiteSpace(address.DisplayName)
                ? null
                : address.DisplayName.Trim().Trim('"');
            return (displayName, emailAddress, localPart);
        }
        catch (FormatException)
        {
            var trimmed = sender.Trim();
            if (!trimmed.Contains('@', StringComparison.Ordinal))
            {
                return (trimmed, null, null);
            }

            var emailAddress = trimmed.ToLowerInvariant();
            var localPart = emailAddress.Split('@').FirstOrDefault();
            return (null, emailAddress, localPart);
        }
    }

    private static string? NormalizeContactName(string? displayName, string? localPart)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var cleanedDisplayName = CollapseWhitespace(displayName);
            return LooksGenericMailboxLabel(cleanedDisplayName) ? null : cleanedDisplayName;
        }

        if (string.IsNullOrWhiteSpace(localPart))
        {
            return null;
        }

        var normalizedLocalPart = localPart.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        normalizedLocalPart = CollapseWhitespace(normalizedLocalPart);
        if (LooksGenericMailboxLabel(normalizedLocalPart))
        {
            return null;
        }

        var parts = normalizedLocalPart
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        if (parts.Any(part => part.Length < 2))
        {
            return null;
        }

        return string.Join(
            " ",
            parts.Select(
                part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static RecruitingContactType? ResolveContactType(
        string? displayName,
        string? localPart,
        InboxEmail inboxEmail,
        ParsedRecruitingEmail parsed)
    {
        var signalText = string.Join(
            " ",
            new[]
            {
                displayName,
                localPart,
                inboxEmail.Subject,
                inboxEmail.ParsedBody
            }.Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        if (signalText.Contains("hiring manager", StringComparison.Ordinal))
        {
            return RecruitingContactType.HiringManager;
        }

        if (signalText.Contains("coordinator", StringComparison.Ordinal) ||
            signalText.Contains("scheduling", StringComparison.Ordinal) ||
            signalText.Contains("scheduler", StringComparison.Ordinal))
        {
            return RecruitingContactType.Coordinator;
        }

        if (signalText.Contains("interviewer", StringComparison.Ordinal) ||
            signalText.Contains("panelist", StringComparison.Ordinal))
        {
            return RecruitingContactType.Interviewer;
        }

        if (signalText.Contains("recruiter", StringComparison.Ordinal) ||
            signalText.Contains("talent partner", StringComparison.Ordinal) ||
            signalText.Contains("talent acquisition", StringComparison.Ordinal) ||
            signalText.Contains("sourcer", StringComparison.Ordinal))
        {
            return RecruitingContactType.Recruiter;
        }

        if (parsed.Category == EmailCategory.RecruiterOutreach)
        {
            return RecruitingContactType.Recruiter;
        }

        return null;
    }

    private static string? ResolveRoleHint(
        string? displayName,
        string? localPart,
        InboxEmail inboxEmail,
        RecruitingContactType? contactType)
    {
        var signalText = string.Join(
            " ",
            new[]
            {
                displayName,
                localPart,
                inboxEmail.Subject,
                inboxEmail.ParsedBody
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var explicitRoleMatch = ContactRoleRegex().Match(signalText);
        if (explicitRoleMatch.Success)
        {
            return CollapseWhitespace(explicitRoleMatch.Value);
        }

        return contactType switch
        {
            RecruitingContactType.Recruiter => "Recruiter",
            RecruitingContactType.HiringManager => "Hiring Manager",
            RecruitingContactType.Coordinator => "Recruiting Coordinator",
            RecruitingContactType.Interviewer => "Interviewer",
            _ => null
        };
    }

    private static decimal ResolveConfidence(
        string? emailAddress,
        string? contactName,
        RecruitingContactType? contactType,
        string? roleHint)
    {
        decimal confidence = 0.35m;

        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            confidence += 0.20m;
        }

        if (!string.IsNullOrWhiteSpace(contactName))
        {
            confidence += 0.15m;
        }

        if (contactType.HasValue)
        {
            confidence += 0.20m;
        }

        if (!string.IsNullOrWhiteSpace(roleHint))
        {
            confidence += 0.05m;
        }

        return Math.Min(confidence, 0.95m);
    }

    private static bool LooksGenericMailboxLabel(string value)
    {
        var normalized = value.ToLowerInvariant();
        return GenericMailboxTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string ToLabel(RecruitingContactType contactType)
    {
        return contactType switch
        {
            RecruitingContactType.Recruiter => "a recruiter",
            RecruitingContactType.HiringManager => "a hiring manager",
            RecruitingContactType.Coordinator => "a recruiting coordinator",
            RecruitingContactType.Interviewer => "an interviewer",
            _ => "a recruiting contact"
        };
    }

    [GeneratedRegex(@"(?:technical recruiter|senior recruiter|staff recruiter|recruiting coordinator|talent partner|talent acquisition partner|hiring manager|engineering manager|interviewer|sourcer)", RegexOptions.IgnoreCase)]
    private static partial Regex ContactRoleRegex();
}

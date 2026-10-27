using System.Text.RegularExpressions;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services.Agents;

public static partial class InboxTriageShortcutRules
{
    private static readonly (string RuleId, Regex Pattern)[] SubjectRules =
    [
        ("verification_code", VerificationCodeRegex()),
        ("receipt", ReceiptRegex()),
        ("security_notice", SecurityNoticeRegex()),
        ("statement_ready", StatementReadyRegex())
    ];

    private static readonly (string RuleId, string Fragment)[] SenderRules =
    [
        ("google_verification", "noreply@google.com"),
        ("stripe_receipt", "stripe.com"),
        ("aws_billing", "aws.amazon.com"),
        ("github_security", "noreply@github.com")
    ];

    public static InboxTriageShortcutMatch? Match(InboxEmail inboxEmail)
    {
        var sender = inboxEmail.Sender?.Trim() ?? string.Empty;
        var subject = inboxEmail.Subject?.Trim() ?? string.Empty;

        foreach (var rule in SenderRules)
        {
            if (sender.Contains(rule.Fragment, StringComparison.OrdinalIgnoreCase))
            {
                return new InboxTriageShortcutMatch(rule.RuleId, $"Matched routine sender {rule.Fragment}.");
            }
        }

        foreach (var rule in SubjectRules)
        {
            if (rule.Pattern.IsMatch(subject))
            {
                return new InboxTriageShortcutMatch(rule.RuleId, $"Matched routine subject pattern {rule.RuleId}.");
            }
        }

        return null;
    }

    [GeneratedRegex("^Your verification code", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerificationCodeRegex();

    [GeneratedRegex("^Receipt for", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptRegex();

    [GeneratedRegex("^\\[Security\\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecurityNoticeRegex();

    [GeneratedRegex("^Your.*statement is ready", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatementReadyRegex();
}

public sealed record InboxTriageShortcutMatch(
    string RuleId,
    string Reason);

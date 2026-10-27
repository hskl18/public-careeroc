using System.Text.RegularExpressions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Application.Services.Agents;

public sealed class ScamCheckerAgentService
{
    private const int MaxReasonLength = 280;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] PaymentSignals =
    [
        "cashier's check",
        "cashiers check",
        "mobile deposit",
        "deposit the check",
        "money order",
        "wire transfer",
        "zelle",
        "venmo",
        "cash app",
        "apple cash",
        "western union",
        "moneygram",
        "bitcoin",
        "crypto",
        "gift card",
        "steam card",
        "itunes card",
        "google play card",
        "purchase equipment",
        "buy equipment",
        "equipment vendor",
        "send funds",
        "refund the balance"
    ];

    private static readonly string[] SensitiveInfoSignals =
    [
        "social security number",
        "ssn",
        "routing number",
        "bank account",
        "passport number",
        "driver's license",
        "drivers license",
        "front and back of your id",
        "copy of your id",
        "direct deposit form"
    ];

    private static readonly string[] ExternalChatSignals =
    [
        "telegram",
        "signal app",
        "whatsapp",
        "google hangouts",
        "skype interview",
        "microsoft teams interview"
    ];

    private static readonly string[] UnrealisticSignals =
    [
        "no interview required",
        "start immediately",
        "work from home",
        "$80 per hour",
        "$90 per hour",
        "$100 per hour",
        "training check",
        "paid training check",
        "personal assistant",
        "data entry clerk"
    ];

    private static readonly string[] FreeMailDomains =
    [
        "gmail.com",
        "yahoo.com",
        "outlook.com",
        "hotmail.com",
        "icloud.com",
        "aol.com",
        "proton.me",
        "protonmail.com"
    ];

    public ScamRiskDecision Check(InboxEmail inboxEmail, ParsedRecruitingEmail parsed)
    {
        var text = Normalize(
            string.Join(
                " ",
                inboxEmail.Sender,
                inboxEmail.Subject,
                inboxEmail.ParsedBody,
                inboxEmail.RawBody,
                parsed.WorkflowNote));
        var signals = new List<string>();

        AddSignals(text, PaymentSignals, signals, "payment/equipment-money request");
        AddSignals(text, SensitiveInfoSignals, signals, "sensitive identity or banking request");
        AddSignals(text, ExternalChatSignals, signals, "external chat channel request");
        AddSignals(text, UnrealisticSignals, signals, "unusual job promise or wording");

        if (UsesFreeMailSender(inboxEmail.Sender))
        {
            signals.Add("sender uses a consumer email domain");
        }

        if (LooksLikeMismatchedSender(inboxEmail.Sender, parsed.Company))
        {
            signals.Add("sender domain does not resemble the extracted company");
        }

        var distinctSignals = signals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        var highRisk = distinctSignals.Any(signal =>
            signal.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            signal.Contains("banking", StringComparison.OrdinalIgnoreCase));
        var sensitiveRisk = distinctSignals.Any(signal =>
            signal.Contains("identity", StringComparison.OrdinalIgnoreCase));
        var suspiciousContext =
            parsed.Category is EmailCategory.RecruiterOutreach
                or EmailCategory.InterviewInvitation
                or EmailCategory.Offer
                or EmailCategory.GenericUpdate;

        if (highRisk || sensitiveRisk || (suspiciousContext && distinctSignals.Count >= 2))
        {
            var riskLevel = highRisk || sensitiveRisk ? "high" : "medium";
            var reason = Truncate(
                $"Scam checker found {string.Join(", ", distinctSignals)}.",
                MaxReasonLength);
            return new ScamRiskDecision(
                true,
                riskLevel,
                riskLevel == "high"
                    ? "Scam Checker Agent blocked automation because the email asks for money, equipment purchase, or sensitive information."
                    : "Scam Checker Agent blocked automation because the recruiting message has suspicious trust signals.",
                reason,
                riskLevel == "high" ? 0.92m : 0.78m,
                distinctSignals);
        }

        return distinctSignals.Count == 0
            ? ScamRiskDecision.Clear
            : new ScamRiskDecision(
                false,
                "low",
                "Scam Checker Agent found weak signals but not enough to block workflow automation.",
                string.Join(", ", distinctSignals),
                0.55m,
                distinctSignals);
    }

    private static void AddSignals(
        string text,
        IReadOnlyList<string> patterns,
        List<string> signals,
        string signal)
    {
        if (patterns.Any(pattern => text.Contains(pattern, StringComparison.Ordinal)))
        {
            signals.Add(signal);
        }
    }

    private static bool UsesFreeMailSender(string sender)
    {
        var domain = ExtractDomain(sender);
        return !string.IsNullOrWhiteSpace(domain) &&
               FreeMailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMismatchedSender(string sender, string company)
    {
        var domain = ExtractDomain(sender);
        if (string.IsNullOrWhiteSpace(domain) ||
            string.IsNullOrWhiteSpace(company) ||
            string.Equals(company, ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase) ||
            UsesFreeMailSender(sender))
        {
            return false;
        }

        var companyTokens = Regex
            .Split(company.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(token => token.Length >= 4)
            .ToList();

        return companyTokens.Count > 0 &&
               !companyTokens.Any(token => domain.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractDomain(string sender)
    {
        var atIndex = sender.LastIndexOf('@');
        if (atIndex < 0 || atIndex == sender.Length - 1)
        {
            return string.Empty;
        }

        var domain = sender[(atIndex + 1)..]
            .Trim()
            .TrimEnd('>', ')', ']', '.', ',')
            .ToLowerInvariant();
        return domain;
    }

    private static string Normalize(string value)
    {
        return Whitespace.Replace(value.ToLowerInvariant(), " ");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

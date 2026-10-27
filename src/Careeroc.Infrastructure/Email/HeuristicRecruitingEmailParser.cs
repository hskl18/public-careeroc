using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Infrastructure.Email;

public sealed partial class HeuristicRecruitingEmailParser : IRecruitingEmailParser
{
    private const string RolePattern = @"[A-Za-z][A-Za-z0-9/&+\- ]{1,80}?";
    private const string RoleBoundary = @"(?=\s(?:role|position|opening|opportunity|at\b|by\b|on\b|next\b)|[.,:;]|$)";
    private const string AssessmentTitleRolePattern =
        @"(?:(?:Software|Data|Machine Learning|ML|AI|Product|Backend|Frontend|Full Stack|Security|Infrastructure|Infra|Research|Business|Operations|Quality Assurance|Risk|Controls|Automation|Analytics|Engineering|Technical)\s+){0,5}(?:Engineer|Developer|Intern|Internship|Analyst|Scientist|Researcher|Specialist|Program|Coordinator|Manager)";

    private static readonly IReadOnlyDictionary<string, string> CompanyOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = "OpenAI",
            ["stripe"] = "Stripe",
            ["ramp"] = "Ramp",
            ["figma"] = "Figma",
            ["notion"] = "Notion",
            ["vercel"] = "Vercel",
            ["atlassian"] = "Atlassian",
            ["nvidia"] = "NVIDIA"
        };

    private static readonly string[] OfferKeywords =
    [
        "extend an offer",
        "offer letter",
        "official offer",
        "pleased to offer",
        "verbal offer"
    ];

    private static readonly string[] RejectionKeywords =
    [
        "not be moving forward",
        "decided not to move forward",
        "not to move forward",
        "regret to inform",
        "moving forward with other candidates",
        "move forward with other candidates",
        "unable to proceed",
        "not selected",
        "unfortunately"
    ];

    private static readonly string[] AssessmentKeywords =
    [
        "online assessment",
        "assessment invitation",
        "complete the assessment",
        "complete this assessment",
        "complete your assessment",
        "technical assessment",
        "your assessment",
        "codesignal",
        "codility",
        "hackerrank",
        "take-home",
        "take home",
        "coding assessment"
    ];

    private static readonly string[] InterviewInvitationKeywords =
    [
        "interview invitation",
        "interview availability",
        "interview availability request",
        "invite you to interview",
        "invite you to a zoom interview",
        "interview via zoom",
        "schedule your first interview",
        "schedule an interview",
        "schedule time",
        "select a time",
        "share your availability",
        "dates with corresponding times",
        "corresponding times and local time zone",
        "phone screen",
        "recruiter screen",
        "schedule a first interview",
        "schedule a technical interview",
        "onsite interview",
        "virtual onsite",
        "final round interview"
    ];

    private static readonly string[] InterviewConfirmationKeywords =
    [
        "confirmation of your interview",
        "interview confirmation",
        "interview confirmed",
        "your interview is confirmed",
        "calendar invite",
        "looking forward to speaking",
        "looking forward to meeting",
        "see you on",
        "call is confirmed",
        "is confirmed for"
    ];

    private static readonly string[] ApplicationReceivedKeywords =
    [
        "received your application",
        "application received",
        "thank you for applying",
        "application has been received",
        "your application was submitted",
        "your application to",
        "thank you for your interest in"
    ];

    private static readonly string[] RecruiterOutreachKeywords =
    [
        "open to a quick intro chat",
        "thought you could be a strong fit",
        "would love to connect",
        "reaching out about",
        "reaching out regarding",
        "came across your background",
        "came across your profile",
        "quick chat this week",
        "interested in learning more about your background",
        "potential fit",
        "partnering with a confidential client",
        "open to a quick call"
    ];

    // Agency recruiters often withhold the employer; their own domain is not the company.
    private static readonly string[] UndisclosedEmployerPhrases =
    [
        "confidential client",
        "undisclosed client",
        "stealth company",
        "stealth startup"
    ];

    private static readonly string[] GenericUpdateKeywords =
    [
        "quick update on your candidacy",
        "next steps regarding your candidacy",
        "will be in touch soon",
        "still under consideration",
        "update on your application"
    ];

    private static readonly HashSet<string> GenericCompanyWords =
    [
        "ashbyhq",
        "academic",
        "board",
        "boards",
        "careers",
        "greenhouse",
        "greenhousemail",
        "hiring",
        "icims",
        "jobs",
        "lever",
        "myworkdayjobs",
        "notifications",
        "noreply",
        "recruiting",
        "smartrecruiters",
        "talent",
        "team",
        "updates",
        "workablemail",
        "workday"
    ];

    public ParsedRecruitingEmail Parse(InboxEmail inboxEmail)
    {
        var content = $"{inboxEmail.Subject}\n{inboxEmail.ParsedBody}";
        var category = Classify(content);
        var company = ExtractCompany(inboxEmail, content);
        var role = ExtractRole(content, company, category);
        var dueDate = ExtractDueDate(inboxEmail, content);
        var actionRequired = ResolveActionRequired(category, dueDate);
        var classificationConfidence = CalculateClassificationConfidence(category, company, role, dueDate);

        return new ParsedRecruitingEmail(
            category,
            ToRecommendedStatus(category),
            company,
            role,
            actionRequired,
            dueDate,
            classificationConfidence,
            BuildWorkflowNote(content, category, company, role, dueDate),
            "Heuristic parser",
            null);
    }

    public Task<ParsedRecruitingEmail> ParseAsync(
        InboxEmail inboxEmail,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Parse(inboxEmail));
    }

    private static EmailCategory Classify(string content)
    {
        var normalized = NormalizeContent(content);

        if (ContainsAny(normalized, OfferKeywords))
        {
            return EmailCategory.Offer;
        }

        if (ContainsAny(normalized, RejectionKeywords))
        {
            return EmailCategory.Rejection;
        }

        if (ContainsAny(normalized, AssessmentKeywords) ||
            RecruitingActionSignals.IsQuestionnaireOrFormRequest(normalized))
        {
            return EmailCategory.OaInvitation;
        }

        if (ContainsAny(normalized, InterviewInvitationKeywords))
        {
            return EmailCategory.InterviewInvitation;
        }

        if (ContainsAny(normalized, InterviewConfirmationKeywords))
        {
            return EmailCategory.InterviewConfirmation;
        }

        if (ContainsAny(normalized, ApplicationReceivedKeywords))
        {
            return EmailCategory.ApplicationReceived;
        }

        if (ContainsAny(normalized, RecruiterOutreachKeywords))
        {
            return EmailCategory.RecruiterOutreach;
        }

        if (ContainsAny(normalized, GenericUpdateKeywords))
        {
            return EmailCategory.GenericUpdate;
        }

        return EmailCategory.GenericUpdate;
    }

    private static ApplicationStatus ToRecommendedStatus(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.ApplicationReceived => ApplicationStatus.Applied,
            EmailCategory.RecruiterOutreach => ApplicationStatus.RecruiterContact,
            EmailCategory.OaInvitation => ApplicationStatus.OaPending,
            EmailCategory.InterviewInvitation => ApplicationStatus.Interviewing,
            EmailCategory.InterviewConfirmation => ApplicationStatus.Interviewing,
            EmailCategory.Rejection => ApplicationStatus.Rejected,
            EmailCategory.Offer => ApplicationStatus.Offer,
            _ => ApplicationStatus.Applied
        };
    }

    private static string BuildWorkflowNote(
        string content,
        EmailCategory category,
        string company,
        string role,
        DateTimeOffset? dueDate)
    {
        return category switch
        {
            EmailCategory.OaInvitation when RecruitingActionSignals.IsQuestionnaireOrFormRequest(content) =>
                $"Detected a candidate questionnaire or form request for {company} / {role}.",
            EmailCategory.OaInvitation when dueDate.HasValue =>
                $"Detected an assessment deadline for {company} / {role} on {dueDate:yyyy-MM-dd}.",
            EmailCategory.InterviewInvitation =>
                $"Detected an interview scheduling request for {company} / {role}.",
            EmailCategory.RecruiterOutreach =>
                $"Detected recruiter outreach for {company} / {role}.",
            EmailCategory.Rejection =>
                $"Detected a rejection for {company} / {role}.",
            EmailCategory.Offer =>
                $"Detected an offer for {company} / {role}.",
            _ =>
                $"Parsed job-related email for {company} / {role}."
        };
    }

    private static string ExtractCompany(InboxEmail inboxEmail, string content)
    {
        if (ContainsAny(NormalizeContent(content), UndisclosedEmployerPhrases))
        {
            return ApplicationDefaults.UnknownCompany;
        }

        foreach (var pattern in CompanyPatterns())
        {
            var match = Regex.Match(content, pattern);
            if (match.Success && TryNormalizeCompany(match.Groups["company"].Value, out var company))
            {
                return company;
            }
        }

        if (CompanyNameNormalizer.TryInferFromEvidence(
                [content],
                out var evidenceCompany,
                allowEmailAddressInference: false))
        {
            return evidenceCompany;
        }

        if (CompanyNameNormalizer.TryInferFromEvidence([inboxEmail.Sender], out var senderEvidenceCompany) &&
            !ShouldRejectInstitutionalSenderInference(inboxEmail, senderEvidenceCompany))
        {
            return senderEvidenceCompany;
        }

        var senderDisplayName = ExtractCompanyFromSenderDisplayName(inboxEmail.Sender);
        if (TryNormalizeCompany(senderDisplayName, out var senderCompany) &&
            !ShouldRejectInstitutionalSenderInference(inboxEmail, senderCompany))
        {
            return senderCompany;
        }

        var fallback = ExtractCompanyFromSenderDomain(inboxEmail.Sender);
        if (TryNormalizeCompany(fallback, out var normalizedCompany) &&
            !ShouldRejectInstitutionalSenderInference(inboxEmail, normalizedCompany))
        {
            return normalizedCompany;
        }

        return ApplicationDefaults.UnknownCompany;
    }

    private static string ExtractRole(string content, string company, EmailCategory category)
    {
        var patterns = new[]
        {
            $@"interest in the (?<role>[A-Z][A-Za-z0-9/&+\- ]{{1,80}}? Program)\s+at {Regex.Escape(company)}",
            $@"for the (?<role>[A-Z][A-Za-z0-9/&+\- ]{{1,80}}? Program)\s+at {Regex.Escape(company)}",
            $@"your application for (?<role>{RolePattern}){RoleBoundary}\s+at {Regex.Escape(company)}",
            $@"application received for (?<role>{RolePattern}){RoleBoundary}\s+at {Regex.Escape(company)}",
            $@"assessment for (?<role>{RolePattern}){RoleBoundary}",
            $@"interview (?:for|with) (?<role>{RolePattern}){RoleBoundary}",
            $@"for the (?<role>{RolePattern}){RoleBoundary}\s+role",
            $@"for (?<role>{RolePattern}){RoleBoundary}\s+at {Regex.Escape(company)}",
            $@"for the (?<role>{RolePattern}){RoleBoundary}\s+at {Regex.Escape(company)}",
            $@"candidacy for the (?<role>{RolePattern}){RoleBoundary}\s+role",
            $@"offer to you for the (?<role>{RolePattern}){RoleBoundary}\s+role",
            $@"for the (?<role>{RolePattern}){RoleBoundary}\s+position",
            $@"regarding the (?<role>{RolePattern}){RoleBoundary}\s+role",
            $@"regarding the (?<role>{RolePattern}){RoleBoundary}\s+position",
            $@"about the (?<role>{RolePattern}){RoleBoundary}\s+role",
            $@"about the (?<role>{RolePattern}){RoleBoundary}\s+position",
            $@"(?<role>{RolePattern}){RoleBoundary}\s+position at {Regex.Escape(company)}",
            $@"(?<role>{RolePattern}){RoleBoundary}\s+opening at {Regex.Escape(company)}"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var cleanedRole = CleanRole(match.Groups["role"].Value);
                if (!IsUsableRole(cleanedRole))
                {
                    return ApplicationDefaults.UnknownRole;
                }

                return cleanedRole;
            }
        }

        var titleMatch = JobTitleRegex().Match(content);
        if (titleMatch.Success)
        {
            var cleanedRole = CleanRole(titleMatch.Groups["role"].Value);
            return IsUsableRole(cleanedRole)
                ? cleanedRole
                : ApplicationDefaults.UnknownRole;
        }

        if (category == EmailCategory.Offer && IsOfferPaperwork(content))
        {
            return "Offer Letter / Hire Paperwork";
        }

        return ApplicationDefaults.UnknownRole;
    }

    private static bool IsOfferPaperwork(string content)
    {
        var normalized = NormalizeContent(content);

        return normalized.Contains("offer letter") ||
               normalized.Contains("hire paperwork") ||
               normalized.Contains("onboarding solutions");
    }

    private static string CleanRole(string role)
    {
        var cleaned = role
            .Replace("the ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("role", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("position", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("opening", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("opportunity", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '.', ':');

        return Regex.Replace(cleaned, @"\s+", " ");
    }

    private static bool IsUsableRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role) ||
            string.Equals(role.Trim(), ApplicationDefaults.UnknownRole, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is not ("you" or "your" or "me" or "candidate" or "applicant");
    }

    private static DateTimeOffset? ExtractDueDate(InboxEmail inboxEmail, string content)
    {
        var anchorDate = inboxEmail.ReceivedAtUtc == default
            ? (DateOnly?)null
            : DateOnly.FromDateTime(inboxEmail.ReceivedAtUtc.UtcDateTime);

        foreach (Match match in DayMonthTimeDueDateRegex().Matches(content))
        {
            var dayMonthDueDate = TryParseDayMonthTimeDueDate(match);
            if (dayMonthDueDate.HasValue)
            {
                return dayMonthDueDate;
            }
        }

        foreach (Match match in AbsoluteDueDateRegex().Matches(content))
        {
            var absoluteDueDate = TryParseAbsoluteDueDate(match.Groups["date"].Value, anchorDate);
            if (absoluteDueDate.HasValue)
            {
                return absoluteDueDate;
            }
        }

        foreach (Match match in NumericDueDateRegex().Matches(content))
        {
            var numericDueDate = TryParseNumericDueDate(match.Groups["date"].Value, anchorDate);
            if (numericDueDate.HasValue)
            {
                return numericDueDate;
            }
        }

        foreach (Match match in RelativeDueDateRegex().Matches(content))
        {
            var relativeDueDate = TryParseRelativeDueDate(match, anchorDate);
            if (relativeDueDate.HasValue)
            {
                return relativeDueDate;
            }
        }

        foreach (Match match in RelativeWindowDueDateRegex().Matches(content))
        {
            var windowDueDate = TryParseRelativeWindowDueDate(match, inboxEmail.ReceivedAtUtc);
            if (windowDueDate.HasValue)
            {
                return windowDueDate;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryParseDayMonthTimeDueDate(Match match)
    {
        var normalizedDate = match.Groups["date"].Value
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Trim();
        var formats = new[] { "d MMMM yyyy", "d MMM yyyy" };

        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(
                    normalizedDate,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                continue;
            }

            if (!match.Groups["time"].Success)
            {
                return ToUtcDate(new DateOnly(parsedDate.Year, parsedDate.Month, parsedDate.Day));
            }

            if (!TimeOnly.TryParseExact(
                    match.Groups["time"].Value,
                    "H:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedTime))
            {
                return ToUtcDate(new DateOnly(parsedDate.Year, parsedDate.Month, parsedDate.Day));
            }

            var hour = parsedTime.Hour;
            if (match.Groups["ampm"].Success)
            {
                var ampm = match.Groups["ampm"].Value;
                if (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
                {
                    hour += 12;
                }
                else if (ampm.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
                {
                    hour = 0;
                }
            }

            var offset = ResolveTimezoneOffset(match.Groups["tz"].Value);
            return new DateTimeOffset(
                    parsedDate.Year,
                    parsedDate.Month,
                    parsedDate.Day,
                    hour,
                    parsedTime.Minute,
                    0,
                    offset)
                .ToUniversalTime();
        }

        return null;
    }

    private static TimeSpan ResolveTimezoneOffset(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "PDT" => TimeSpan.FromHours(-7),
            "PST" => TimeSpan.FromHours(-8),
            "MDT" => TimeSpan.FromHours(-6),
            "MST" => TimeSpan.FromHours(-7),
            "CDT" => TimeSpan.FromHours(-5),
            "CST" => TimeSpan.FromHours(-6),
            "EDT" => TimeSpan.FromHours(-4),
            "EST" => TimeSpan.FromHours(-5),
            _ => TimeSpan.Zero
        };
    }

    private static DateTimeOffset? TryParseAbsoluteDueDate(string rawDate, DateOnly? anchorDate)
    {
        var normalized = Regex.Replace(rawDate, @"(?<=\d)(st|nd|rd|th)\b", string.Empty, RegexOptions.IgnoreCase)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Trim();
        var formats = new[] { "MMMM d yyyy", "MMM d yyyy", "MMMM d", "MMM d" };

        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(
                    normalized,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                continue;
            }

            if (format.EndsWith("yyyy", StringComparison.Ordinal))
            {
                return ToUtcDate(new DateOnly(parsedDate.Year, parsedDate.Month, parsedDate.Day));
            }

            if (!anchorDate.HasValue)
            {
                return null;
            }

            var resolvedDate = new DateOnly(anchorDate.Value.Year, parsedDate.Month, parsedDate.Day);
            if (resolvedDate < anchorDate.Value.AddDays(-180))
            {
                resolvedDate = resolvedDate.AddYears(1);
            }

            return ToUtcDate(resolvedDate);
        }

        return null;
    }

    private static DateTimeOffset? TryParseNumericDueDate(string rawDate, DateOnly? anchorDate)
    {
        var normalized = rawDate.Trim();
        var formats = new[] { "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy", "M/d", "MM/dd" };

        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(
                    normalized,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                continue;
            }

            if (format.Contains("y", StringComparison.Ordinal))
            {
                return ToUtcDate(new DateOnly(parsedDate.Year, parsedDate.Month, parsedDate.Day));
            }

            if (!anchorDate.HasValue)
            {
                return null;
            }

            var resolvedDate = new DateOnly(anchorDate.Value.Year, parsedDate.Month, parsedDate.Day);
            if (resolvedDate < anchorDate.Value.AddDays(-180))
            {
                resolvedDate = resolvedDate.AddYears(1);
            }

            return ToUtcDate(resolvedDate);
        }

        return null;
    }

    private static DateTimeOffset? TryParseRelativeDueDate(Match match, DateOnly? anchorDate)
    {
        if (!anchorDate.HasValue)
        {
            return null;
        }

        if (match.Groups["relative"].Success)
        {
            var relative = match.Groups["relative"].Value;
            var resolvedDate = relative.Equals("tomorrow", StringComparison.OrdinalIgnoreCase)
                ? anchorDate.Value.AddDays(1)
                : anchorDate.Value;

            return ToUtcDate(resolvedDate);
        }

        var weekdayToken = match.Groups["weekday"].Success
            ? match.Groups["weekday"].Value
            : match.Groups["weekdayOnly"].Value;
        if (!Enum.TryParse<DayOfWeek>(
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(weekdayToken),
                out var weekday))
        {
            return null;
        }

        var offsetDays = ((int)weekday - (int)anchorDate.Value.DayOfWeek + 7) % 7;
        if (match.Groups["modifier"].Success &&
            match.Groups["modifier"].Value.Equals("next", StringComparison.OrdinalIgnoreCase))
        {
            offsetDays = offsetDays == 0 ? 7 : offsetDays;
        }

        return ToUtcDate(anchorDate.Value.AddDays(offsetDays));
    }

    private static DateTimeOffset? TryParseRelativeWindowDueDate(Match match, DateTimeOffset receivedAtUtc)
    {
        if (receivedAtUtc == default ||
            !TryParseSmallNumber(match.Groups["amount"].Value, out var amount) ||
            amount <= 0)
        {
            return null;
        }

        var isHours = match.Groups["unit"].Value.StartsWith("hour", StringComparison.OrdinalIgnoreCase);
        if (isHours)
        {
            return receivedAtUtc.ToUniversalTime().AddHours(amount);
        }

        var isBusinessDays = match.Groups["business"].Success;
        var dueDate = isBusinessDays
            ? AddBusinessDays(receivedAtUtc.ToUniversalTime(), amount)
            : receivedAtUtc.ToUniversalTime().AddDays(amount);

        return dueDate;
    }

    private static bool TryParseSmallNumber(string value, out int number)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        number = value.ToLowerInvariant() switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            _ => 0
        };

        return number > 0;
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset start, int businessDays)
    {
        var dueDate = start;
        var remainingDays = businessDays;

        while (remainingDays > 0)
        {
            dueDate = dueDate.AddDays(1);
            if (dueDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            remainingDays--;
        }

        return dueDate;
    }

    private static DateTimeOffset ToUtcDate(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static bool ResolveActionRequired(EmailCategory category, DateTimeOffset? dueDate)
    {
        if (category is EmailCategory.Rejection or EmailCategory.ApplicationReceived)
        {
            return false;
        }

        if (category == EmailCategory.GenericUpdate && !dueDate.HasValue)
        {
            return false;
        }

        return true;
    }

    private static decimal CalculateClassificationConfidence(
        EmailCategory category,
        string company,
        string role,
        DateTimeOffset? dueDate)
    {
        var confidence = category switch
        {
            EmailCategory.Offer => 0.98m,
            EmailCategory.Rejection => 0.96m,
            EmailCategory.OaInvitation => dueDate.HasValue ? 0.95m : 0.90m,
            EmailCategory.InterviewInvitation => 0.93m,
            EmailCategory.InterviewConfirmation => 0.90m,
            EmailCategory.RecruiterOutreach => 0.88m,
            EmailCategory.ApplicationReceived => 0.89m,
            EmailCategory.GenericUpdate => 0.42m,
            _ => 0.60m
        };

        if (role == ApplicationDefaults.UnknownRole)
        {
            confidence = category switch
            {
                EmailCategory.Offer => Math.Min(confidence, 0.90m),
                EmailCategory.Rejection => Math.Min(confidence, 0.88m),
                EmailCategory.OaInvitation => Math.Min(confidence, 0.82m),
                EmailCategory.InterviewInvitation => Math.Min(confidence, 0.80m),
                EmailCategory.InterviewConfirmation => Math.Min(confidence, 0.78m),
                EmailCategory.ApplicationReceived => Math.Min(confidence, 0.76m),
                EmailCategory.RecruiterOutreach => Math.Min(confidence, 0.72m),
                EmailCategory.GenericUpdate => Math.Min(confidence, 0.42m),
                _ => Math.Min(confidence, 0.60m)
            };
        }

        if (company == ApplicationDefaults.UnknownCompany)
        {
            confidence = Math.Min(confidence, 0.55m);
        }

        return confidence;
    }

    private static bool ContainsAny(string normalized, IEnumerable<string> keywords)
    {
        return keywords.Any(normalized.Contains);
    }

    private static string NormalizeContent(string content)
    {
        return Regex.Replace(content.ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private static IEnumerable<string> CompanyPatterns()
    {
        // Words stay on one line: the subject and a "Hi Alex," greeting must not merge into one company name.
        const string companyPattern = @"(?<company>[A-Z][A-Za-z0-9&.'-]*(?:[ \t][A-Z][A-Za-z0-9&.'-]*){0,3})";
        const string assessmentTitleCompanyPattern = @"(?<company>[A-Z][A-Za-z0-9&.'-]*(?:[ \t][A-Z][A-Za-z0-9&.'-]*){0,3}?)";
        const string companyEnd = @"(?=[.,:;]|\r?\n|$)";

        yield return $@"^{assessmentTitleCompanyPattern}\s+{AssessmentTitleRolePattern}\s+(?:Test|Assessment)\b";
        yield return $@"\bHackerRank\s+{assessmentTitleCompanyPattern}\s+{AssessmentTitleRolePattern}\s+(?:Test|Assessment)\b";
        yield return $@"application (?:to|with) {companyPattern}{companyEnd}";
        yield return $@"role at {companyPattern}{companyEnd}";
        yield return $@"position at {companyPattern}{companyEnd}";
        yield return $@"interview (?:for|with) .*? at {companyPattern}{companyEnd}";
        yield return $@"offer .*? at {companyPattern}{companyEnd}";
        yield return $@"from {companyPattern}{companyEnd}";
        yield return $@"Program at {companyPattern}(?=[.!?,:;]|\r?\n|$)";
        yield return $@"{companyPattern} team{companyEnd}";
    }

    private static string ExtractCompanyFromSenderDomain(string sender)
    {
        var domain = sender.Split('@').LastOrDefault() ?? string.Empty;
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (labels.Length == 0)
        {
            return string.Empty;
        }

        var companyToken = labels.Length == 1 ? labels[0] : labels[^2];
        if (labels.Length >= 3 &&
            HostingProviderDomainRegex().IsMatch(domain))
        {
            companyToken = labels[0];
        }

        return companyToken;
    }

    private static string ExtractCompanyFromSenderDisplayName(string sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            return string.Empty;
        }

        try
        {
            var address = new MailAddress(sender.Trim());
            return address.DisplayName;
        }
        catch (FormatException)
        {
            var match = Regex.Match(sender, @"^(?<display>[^<]+)<[^>]+>$");
            return match.Success ? match.Groups["display"].Value : string.Empty;
        }
    }

    private static bool TryNormalizeCompany(string raw, out string company)
    {
        return CompanyNameNormalizer.TryNormalize(raw, out company);
    }

    private static bool ShouldRejectInstitutionalSenderInference(
        InboxEmail inboxEmail,
        string company)
    {
        return CompanyNameNormalizer.IsWeakInstitutionalMailboxCompany(
            company,
            inboxEmail.Sender,
            inboxEmail.InboxAccount?.EmailAddress ??
            inboxEmail.InboxAccount?.User?.Email);
    }

    [GeneratedRegex(@"\b(?<role>(?:(?:senior|staff|lead|principal|junior|associate|sr\.|jr\.)\s+)?(?:(?:software|backend|front\s?end|full[\s-]?stack|product|platform|data|machine learning|ml|ai|security|site reliability|sre|devops|mobile|ios|android|research|design|product design|ux|ui|account|solutions|customer success|program|project|marketing|sales|operations|business)\s+)?(?:engineer|developer|manager|designer|scientist|analyst|architect|coordinator|specialist|recruiter|intern))\b", RegexOptions.IgnoreCase)]
    private static partial Regex JobTitleRegex();

    [GeneratedRegex(@"(?:by|before|on|no later than|deadline(?:\s+is)?|due(?:\s+date)?(?:\s+is)?)\s+(?<date>[A-Z][a-z]{2,}\.?\s\d{1,2}(?:st|nd|rd|th)?(?:,\s*\d{4})?)", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteDueDateRegex();

    [GeneratedRegex(@"(?:by|before|on|no later than|deadline(?:\s+is)?|due(?:\s+date)?(?:\s+is)?)\s+(?<date>\d{1,2}/\d{1,2}(?:/\d{2,4})?)", RegexOptions.IgnoreCase)]
    private static partial Regex NumericDueDateRegex();

    [GeneratedRegex(@"\b(?:end\s+login\s+date/time|end\s+date/time|deadline|due(?:\s+date)?(?:\s+is)?|expires?(?:\s+at|\s+on)?)\s*:?\s*(?<date>\d{1,2}\s+[A-Z][a-z]{2,}\.?\s+\d{4})(?:\s+(?<time>\d{1,2}:\d{2})\s*(?<ampm>AM|PM)?(?:\s+(?<tz>[A-Z]{2,4}))?)?", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthTimeDueDateRegex();

    [GeneratedRegex(@"(?:by|before|on|no later than|deadline(?:\s+is)?|due(?:\s+date)?(?:\s+is)?)\s+(?:(?:the\s+)?(?<relative>today|tomorrow)|(?<modifier>this|next)\s+(?<weekday>monday|tuesday|wednesday|thursday|friday|saturday|sunday)|(?<weekdayOnly>monday|tuesday|wednesday|thursday|friday|saturday|sunday))(?:\s+(?:morning|afternoon|evening|eod|end of day))?", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeDueDateRegex();

    [GeneratedRegex(@"\b(?:within|expires?\s+in|will\s+expire\s+in|you\s+have|deadline(?:\s+is)?\s+in|due\s+in)\s+(?:the\s+next\s+)?(?<amount>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?<business>business\s+)?(?:calendar\s+)?(?<unit>hours?|days?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeWindowDueDateRegex();

    [GeneratedRegex(@"(?:greenhouse|lever|ashbyhq|smartrecruiters|workday|myworkdayjobs|workablemail)", RegexOptions.IgnoreCase)]
    private static partial Regex HostingProviderDomainRegex();
}

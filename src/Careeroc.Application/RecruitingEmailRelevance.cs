namespace Careeroc.Application;

public static class RecruitingEmailRelevance
{
    private static readonly string[] SenderHints =
    [
        "career",
        "careers",
        "recruit",
        "recruiter",
        "recruiting",
        "talent",
        "hiring",
        "jobs",
        "applicant",
        "greenhouse",
        "lever",
        "ashby",
        "ashbyhq",
        "smartrecruiters",
        "icims",
        "workable",
        "workday",
        "myworkdayjobs",
        "successfactors"
    ];

    private static readonly string[] StrongWorkflowPhrases =
    [
        "update on your candidacy",
        "next steps regarding your candidacy",
        "still under consideration",
        "interview invitation",
        "interview confirmation",
        "confirmation of your interview",
        "interview status",
        "interview availability",
        "interview availability request",
        "interview for",
        "interview with",
        "invite you to interview",
        "invite you to a zoom interview",
        "interview via zoom",
        "schedule your interview",
        "schedule an interview",
        "share your availability",
        "send your availability",
        "reply with your availability",
        "please reply with your availability",
        "dates with corresponding times",
        "corresponding times and local time zone",
        "phone screen",
        "final round",
        "virtual onsite",
        "onsite interview",
        "online assessment",
        "technical assessment",
        "complete the assessment",
        "coding challenge",
        "take-home",
        "take home",
        "offer letter",
        "job offer",
        "verbal offer",
        "extend an offer",
        "offer for the",
        "not be moving forward",
        "moving forward with other candidates",
        "talent acquisition",
        "hiring team"
    ];

    private static readonly string[] GenericApplicationPhrases =
    [
        "thank you for applying",
        "thanks for applying",
        "application received",
        "we received your application",
        "your application",
        "update on your application"
    ];

    private static readonly string[] OutreachPhrases =
    [
        "would love to connect",
        "strong fit",
        "reaching out about",
        "reaching out regarding",
        "came across your background",
        "came across your profile",
        "quick intro chat",
        "open to a quick intro chat",
        "learning more about your background",
        "opportunity at",
        "role at",
        "position at",
        "confidential client",
        "open to a quick call"
    ];

    private static readonly string[] RoleTerms =
    [
        "engineer",
        "developer",
        "designer",
        "analyst",
        "scientist",
        "manager",
        "coordinator",
        "specialist",
        "consultant",
        "intern",
        "internship",
        "associate",
        "full time",
        "full-time",
        "new grad",
        "graduate"
    ];

    private static readonly string[] HiringContextTerms =
    [
        "role",
        "position",
        "opportunity",
        "application",
        "interview",
        "recruiter",
        "recruiting",
        "candidate",
        "candidacy",
        "hiring"
    ];

    private static readonly string[] NoisePhrases =
    [
        "security code",
        "verification code",
        "confirmation code",
        "authentication code",
        "one-time code",
        "one time code",
        "copy and paste this code",
        "paste this code",
        "confirm your identity",
        "verify your identity",
        "code into the security code field",
        "new opportunity available",
        "employers are hiring",
        "is hiring",
        "sent you",
        "new messages",
        "catch up on messages",
        "career center",
        "graduate school",
        "application panel",
        "alumni",
        "news digest",
        "unsubscribe",
        "manage preferences",
        "view in browser",
        "privacy policy",
        "terms of service",
        "flash sale",
        "limited time",
        "promo code",
        "discount",
        "coupon",
        "sale ends",
        "shop now",
        "order now",
        "buy now",
        "free shipping",
        "your order",
        "order has",
        "statement",
        "benefits",
        "form crs",
        "dashpass",
        "rewards",
        "mastercard",
        "credit card",
        "rent day",
        "room assignment",
        "express check-out",
        "security profile",
        "mfa code",
        "scholarship",
        "scholarships",
        "undergraduate",
        "student",
        "hotel",
        "fellowship",
        "referral",
        "50% off",
        "invoice",
        "receipt",
        "newsletter",
        "maintenance notice",
        "security alert",
        "transaction",
        "wallet",
        "weekly digest",
        "daily digest",
        "unread messages",
        "catch up on messages",
        "update your preferences",
        "last chance",
        "early bird"
    ];

    private static readonly string[] CareerProgramPhrases =
    [
        "career readiness",
        "career-readiness",
        "career readiness journey",
        "career program",
        "jobs board",
        "jobs board access",
        "mentorship",
        "mentorship experience",
        "cohort",
        "discovery session",
        "discovery sessions",
        "workshop",
        "workshops",
        "join our platform",
        "seeking support in the next steps of your career",
        "first-generation computer science students"
    ];

    private static readonly string[] EventInvitePhrases =
    [
        "career webinar",
        "webinar",
        "join virtually",
        "register here",
        "register for tomorrow",
        "register for today's",
        "don’t miss out",
        "don't miss out"
    ];

    private static readonly string[] EventSenderHints =
    [
        "joinhandshake.com",
        "joinhandshake",
        "handshake"
    ];

    public static bool IsLikelyRecruiting(
        string? sender,
        string? subject,
        string? body)
    {
        var normalizedSender = Normalize(sender);
        var normalizedSubject = Normalize(subject);
        var normalizedBody = Normalize(body);

        if (string.IsNullOrWhiteSpace(normalizedSubject) &&
            string.IsNullOrWhiteSpace(normalizedBody))
        {
            return false;
        }

        var content = $"{normalizedSubject} {normalizedBody}";
        var hasStrongWorkflowSignal = ContainsAny(content, StrongWorkflowPhrases);
        var hasGenericApplicationSignal = ContainsAny(content, GenericApplicationPhrases);
        var hasOutreachSignal = ContainsAny(content, OutreachPhrases);
        var hasDirectRecruitingSignal =
            hasStrongWorkflowSignal ||
            hasGenericApplicationSignal ||
            hasOutreachSignal;

        if (!hasDirectRecruitingSignal &&
            (ContainsAny(content, CareerProgramPhrases) ||
             LooksLikeHandshakeEventInvite(normalizedSender, content) ||
             ContainsAny(content, NoisePhrases)))
        {
            return false;
        }

        var score = 0;

        if (ContainsAny(normalizedSender, SenderHints))
        {
            score += 3;
        }

        if (hasStrongWorkflowSignal)
        {
            score += 4;
        }

        if (hasGenericApplicationSignal)
        {
            score += 2;
        }

        if (hasOutreachSignal)
        {
            score += 3;
        }

        if (ContainsRoleAndHiringContext(content))
        {
            score += 2;
        }

        if (ContainsAny(content, NoisePhrases))
        {
            score -= 4;
        }

        if (ContainsAny(content, CareerProgramPhrases))
        {
            score -= 6;
        }

        if (LooksLikeHandshakeEventInvite(normalizedSender, content))
        {
            score -= 6;
        }

        return score >= 4;
    }

    private static bool LooksLikeHandshakeEventInvite(string sender, string content)
    {
        return ContainsAny(sender, EventSenderHints) &&
               ContainsAny(content, EventInvitePhrases);
    }

    private static bool ContainsRoleAndHiringContext(string content)
    {
        return ContainsAny(content, RoleTerms) &&
               ContainsAny(content, HiringContextTerms);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> fragments)
    {
        return fragments.Any(value.Contains);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value
                .ToLowerInvariant()
                .Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

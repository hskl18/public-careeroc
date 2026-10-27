using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Careeroc.Application;

public static partial class CompanyNameNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> CompanyOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Names whose casing or spacing cannot be recovered from a sender label or domain.
            ["atlassian"] = "Atlassian",
            ["cloudflare"] = "Cloudflare",
            ["figma"] = "Figma",
            ["github"] = "GitHub",
            ["ibm"] = "IBM",
            ["linkedin"] = "LinkedIn",
            ["notion"] = "Notion",
            ["nvidia"] = "NVIDIA",
            ["openai"] = "OpenAI",
            ["paypal"] = "PayPal",
            ["ramp"] = "Ramp",
            ["stripe"] = "Stripe",
            ["tiktok"] = "TikTok",
            ["vercel"] = "Vercel"
        };

    private static readonly HashSet<string> GenericCompanyWords =
    [
        "applicant",
        "application",
        "academic",
        "ashbyhq",
        "board",
        "boards",
        "careers",
        "greenhouse",
        "greenhousemail",
        "gmail",
        "hiring",
        "hr",
        "hotmail",
        "icims",
        "jobs",
        "lever",
        "mail",
        "myworkday",
        "myworkdayjobs",
        "notification",
        "notifications",
        "noreply",
        "no reply",
        "outlook",
        "http",
        "https",
        "recruiting",
        "smartrecruiters",
        "talent",
        "team",
        "at this moment",
        "at this point",
        "at this time",
        "our company",
        "our team",
        "the company",
        "the team",
        "this company",
        "this moment",
        "this point",
        "this time",
        "updates",
        "us",
        "www",
        "your company",
        "workablemail",
        "workday",
        "workday notification",
        "yahoo"
    ];

    public static string Resolve(string rawCompany, IEnumerable<string> evidence)
    {
        var rawIsGeneric = IsGeneric(rawCompany);
        if (TryInferFromEvidence(evidence, out var evidenceCompany) &&
            (rawIsGeneric || ShouldPreferEvidence(rawCompany, evidenceCompany)))
        {
            return evidenceCompany;
        }

        return TryNormalize(rawCompany, out var normalizedCompany)
            ? normalizedCompany
            : ApplicationDefaults.UnknownCompany;
    }

    public static bool TryInferFromEvidence(
        IEnumerable<string> evidence,
        out string company,
        bool allowEmailAddressInference = true)
    {
        foreach (var item in evidence.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (allowEmailAddressInference && TryInferFromEmailAddress(item, out company))
            {
                return true;
            }

            foreach (var pattern in EvidencePatterns())
            {
                var match = Regex.Match(item, pattern, RegexOptions.IgnoreCase);
                if (match.Success && TryNormalize(match.Groups["company"].Value, out company))
                {
                    return true;
                }
            }
        }

        company = string.Empty;
        return false;
    }

    public static bool IsWeakInstitutionalMailboxCompany(
        string company,
        string sender,
        string? mailboxEmail)
    {
        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(mailboxEmail))
        {
            return false;
        }

        if (!TryGetInstitutionKeyFromValue(sender, out var senderInstitutionKey) ||
            !TryGetInstitutionKeyFromValue(mailboxEmail, out var mailboxInstitutionKey))
        {
            return false;
        }

        if (!string.Equals(senderInstitutionKey, mailboxInstitutionKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            NormalizeKey(company),
            senderInstitutionKey,
            StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> ShoutedStopWords = new(StringComparer.OrdinalIgnoreCase) { "of", "and", "the", "for", "de", "la" };

    public static bool TryNormalize(string raw, out string company)
    {
        var normalized = NormalizeRaw(raw);
        if (IsUrlLikeOrProtocolToken(normalized))
        {
            company = string.Empty;
            return false;
        }

        if (IsCommaSeparatedPersonName(normalized))
        {
            company = string.Empty;
            return false;
        }

        if (WorkdayAttributionPattern().Match(normalized) is { Success: true } workdayAttributionMatch &&
            TryNormalize(workdayAttributionMatch.Groups["company"].Value, out company))
        {
            return true;
        }

        if (AttributionPattern().Match(normalized) is { Success: true } attributionMatch &&
            TryNormalize(attributionMatch.Groups["company"].Value, out company))
        {
            return true;
        }

        normalized = GenericPrefixRegex().Replace(normalized, string.Empty);
        normalized = TemporalSuffixRegex().Replace(normalized, string.Empty);
        normalized = GenericSuffixRegex().Replace(normalized, string.Empty);
        normalized = GenericSuffixRegex().Replace(normalized, string.Empty);
        normalized = NormalizeWhitespaceAndTrim(normalized);

        if (string.IsNullOrWhiteSpace(normalized) || IsGeneric(normalized))
        {
            company = string.Empty;
            return false;
        }

        if (CompanyOverrides.TryGetValue(NormalizeKey(normalized), out var overrideValue))
        {
            company = overrideValue;
            return true;
        }

        company = ToDisplayName(normalized);
        return true;
    }

    public static bool IsUsableCompanyName(string? company)
    {
        return !string.IsNullOrWhiteSpace(company) &&
               !string.Equals(company.Trim(), ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase) &&
               !IsUrlLikeOrProtocolToken(company.Trim()) &&
               !IsCommaSeparatedPersonName(company.Trim()) &&
               !IsGeneric(company);
    }

    public static bool IsLikelyPersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeWhitespaceAndTrim(value);
        if (IsCommaSeparatedPersonName(normalized))
        {
            return true;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Length is 2 or 3 &&
               tokens.All(token => PersonNameTokenRegex().IsMatch(token));
    }

    private static bool ShouldPreferEvidence(string rawCompany, string evidenceCompany)
    {
        if (!TryNormalize(rawCompany, out var normalizedRaw))
        {
            return true;
        }

        return normalizedRaw.Contains(evidenceCompany, StringComparison.OrdinalIgnoreCase) &&
               normalizedRaw.Length > evidenceCompany.Length + 3;
    }

    private static bool TryInferFromEmailAddress(string value, out string company)
    {
        if (TryExtractDisplayName(value, out var displayName))
        {
            if (IsCommaSeparatedPersonName(displayName))
            {
                company = string.Empty;
            }
            else if (!IsLikelyPersonName(displayName) &&
                     TryNormalize(displayName, out company))
            {
                return true;
            }
        }

        foreach (var candidate in ExtractEmailCandidates(value))
        {
            var parts = candidate.Split('@', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var localPart = parts[0]
                .Split('+', 2, StringSplitOptions.TrimEntries)[0]
                .Split('.', 2, StringSplitOptions.TrimEntries)[0]
                .Split('-', 2, StringSplitOptions.TrimEntries)[0];
            var domain = parts[1];
            var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (labels.Length == 0)
            {
                continue;
            }

            var token = IsAtsDomain(domain)
                ? localPart
                : labels.Length == 1 ? labels[0] : labels[^2];

            if (token.Length >= 3 && TryNormalize(token, out company))
            {
                return true;
            }
        }

        company = string.Empty;
        return false;
    }

    private static bool TryExtractDisplayName(string value, out string displayName)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            displayName = address.DisplayName;
            return !string.IsNullOrWhiteSpace(displayName);
        }
        catch (FormatException)
        {
            var match = Regex.Match(value, @"^(?<display>[^<]+)<[^>]+>$");
            displayName = match.Success ? match.Groups["display"].Value : string.Empty;
            return !string.IsNullOrWhiteSpace(displayName);
        }
    }

    private static IReadOnlyList<string> ExtractEmailCandidates(string value)
    {
        var candidates = new List<string>();

        try
        {
            var address = new MailAddress(value.Trim());
            if (!string.IsNullOrWhiteSpace(address.Address))
            {
                candidates.Add(address.Address);
            }
        }
        catch (FormatException)
        {
            foreach (Match match in EmailAddressRegex().Matches(value))
            {
                candidates.Add(match.Value);
            }
        }

        return candidates;
    }

    private static bool IsAtsDomain(string domain)
    {
        return AtsDomainRegex().IsMatch(domain);
    }

    private static bool TryGetInstitutionKeyFromValue(string value, out string institutionKey)
    {
        institutionKey = string.Empty;

        foreach (var candidate in ExtractEmailCandidates(value))
        {
            var parts = candidate.Split('@', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var labels = parts[1]
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (labels.Length < 2)
            {
                continue;
            }

            if (string.Equals(labels[^1], "edu", StringComparison.OrdinalIgnoreCase))
            {
                institutionKey = NormalizeKey(labels[^2]);
                return !string.IsNullOrWhiteSpace(institutionKey);
            }

            if (labels.Length >= 3 &&
                string.Equals(labels[^2], "ac", StringComparison.OrdinalIgnoreCase))
            {
                institutionKey = NormalizeKey(labels[^3]);
                return !string.IsNullOrWhiteSpace(institutionKey);
            }
        }

        return false;
    }

    private static bool IsGeneric(string value)
    {
        return GenericCompanyWords.Contains(NormalizeKey(value));
    }

    private static bool IsUrlLikeOrProtocolToken(string value)
    {
        var normalized = NormalizeWhitespaceAndTrim(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var key = NormalizeKey(normalized);
        if (key is "http" or "https" or "www")
        {
            return true;
        }

        if (normalized.Contains("://", StringComparison.Ordinal) ||
            UrlPrefixRegex().IsMatch(normalized) ||
            MultiLabelDomainRegex().IsMatch(normalized) ||
            KnownPlatformDomainRegex().IsMatch(normalized))
        {
            return true;
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Scheme) &&
               uri.Scheme.Length <= 12;
    }

    private static bool IsCommaSeparatedPersonName(string value)
    {
        return CommaSeparatedPersonNameRegex().IsMatch(value);
    }

    private static IEnumerable<string> EvidencePatterns()
    {
        const string companyPattern = @"(?<company>[A-Z][A-Za-z0-9&.'+/%-]*(?:[ \t]+[A-Z][A-Za-z0-9&.'+/%-]*){0,5})";

        yield return $@"\bat\s+{companyPattern}(?=:\s*(?:thank you|application|update)|[.!?,:;]|$)";
        yield return $@"(?:thank|thanks) you for (?:applying|your application)(?:[ \t]+(?:to|at|with|for employment at))[ \t]+{companyPattern}(?=[.!?]|$)";
        yield return $@"(?:application|applying|applied|employment)(?:[ \t]+(?:to|at|with|in))[ \t]+{companyPattern}(?=[.!?,:;]|$)";
        yield return $@"interest[ \t]+(?:in|with)[ \t]+(?!the[ \t]+(?:[A-Za-z0-9&.'+/%-]+[ \t]+){{0,8}}(?:role|position|internship|opportunity)\b){companyPattern}(?=[.!?,:;]|$)";
        yield return $@"(?:role|position|internship|opportunity)[ \t]+at[ \t]+{companyPattern}(?=[.!?,:;]|$)";
        yield return $@"interview[ \t]+with[ \t]+{companyPattern}(?=[.!?,:;]|$)";
        yield return $@"^{companyPattern}\s*:\s*(?:thank you|application|update|job requisition)";
        yield return $@"^{companyPattern}\s+-\s*(?:thank you|application|anti-money|technology|data|it[ \t]+analyst|job requisition)";
        yield return $@"^{companyPattern}\s+jobs\s*:\s*thank you";
    }

    private static string NormalizeRaw(string raw)
    {
        var normalized = raw.Trim().Trim('"', '\'');

        try
        {
            normalized = Uri.UnescapeDataString(normalized.Replace("+", " ", StringComparison.Ordinal));
        }
        catch (UriFormatException)
        {
            normalized = normalized.Replace("+", " ", StringComparison.Ordinal);
        }

        return NormalizeWhitespaceAndTrim(normalized);
    }

    private static string NormalizeWhitespaceAndTrim(string value)
    {
        return Regex.Replace(value, @"\s+", " ")
            .Trim(' ', '.', ',', ':', ';', '-', '–', '—', '/', '|');
    }

    private static string NormalizeKey(string value)
    {
        return NormalizeWhitespaceAndTrim(value)
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string ToDisplayName(string value)
    {
        var normalized = NormalizeWhitespaceAndTrim(value);
        var key = NormalizeKey(normalized);
        if (CompanyOverrides.TryGetValue(key, out var overrideValue))
        {
            return overrideValue;
        }

        var words = normalized.Split(' ');
        var letters = normalized.Where(char.IsLetter).ToArray();
        var isShouted = letters.Length > 0 && letters.All(char.IsUpper);
        if (isShouted && words.Length == 1 && letters.Length <= 4)
        {
            return normalized;
        }

        return string.Join(' ', words.Select(word => ToDisplayWord(word, isShouted)));
    }

    // Keep deliberate casing ("OpenAI", "Kestrel AI"); title-case names that arrive in one case, like domain labels.
    private static string ToDisplayWord(string word, bool isShouted)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return word;
        }

        if (isShouted)
        {
            return letters.Length <= 2 && !ShoutedStopWords.Contains(word)
                ? word
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant());
        }

        return letters.All(char.IsLower)
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word)
            : word;
    }

    [GeneratedRegex(@"^workday\s+@\s+(?<company>[A-Za-z0-9&.,' -]{2,80})(?:\s+notification)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WorkdayAttributionPattern();

    [GeneratedRegex(@"^(?<company>[A-Za-z0-9&.,' -]{2,80})\s+@\s+(?:icims|workday|greenhouse|lever|ashby|smartrecruiters)(?:\s+notification)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AttributionPattern();

    [GeneratedRegex(@"^(?:do\s*not\s*reply|do-not-reply|donotreply|no\s*reply|no-reply|noreply|send\s*only|send-only|workday|notification|notifications)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex GenericPrefixRegex();

    [GeneratedRegex(@"\s+(?:next|this)\s+week$", RegexOptions.IgnoreCase)]
    private static partial Regex TemporalSuffixRegex();

    [GeneratedRegex(@"\s+(?:human resources|people operations|global support|talent acquisition|talent|recruitment|recruiting|scheduling|careers|career|jobs|hr/ap|hr|ap|team|notification|notifications|workday)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericSuffixRegex();

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"(?:greenhouse|lever|ashbyhq|smartrecruiters|workday|myworkdayjobs|workablemail|icims)", RegexOptions.IgnoreCase)]
    private static partial Regex AtsDomainRegex();

    [GeneratedRegex(@"^(?:https?|www)(?:\b|[:/.])", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPrefixRegex();

    [GeneratedRegex(@"^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+){2,}(?::\d{2,5})?(?:/.*)?$")]
    private static partial Regex MultiLabelDomainRegex();

    [GeneratedRegex(@"^(?:greenhouse|greenhouse-mail|myworkdayjobs|workday|lever|ashbyhq|smartrecruiters|workablemail|icims)\.(?:com|io|co)$", RegexOptions.IgnoreCase)]
    private static partial Regex KnownPlatformDomainRegex();

    [GeneratedRegex(@"^[A-Z][A-Za-z'’-]{1,40},\s*[A-Z][A-Za-z'’-]{1,40}(?:\s+[A-Z][A-Za-z'’-]{1,40})?$")]
    private static partial Regex CommaSeparatedPersonNameRegex();

    [GeneratedRegex(@"^[A-Z][a-z]+(?:[-'][A-Z][a-z]+)?$")]
    private static partial Regex PersonNameTokenRegex();
}

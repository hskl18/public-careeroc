using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
    private static ResumeExtraction ExtractResume(string sourceText)
    {
        var lower = sourceText.ToLowerInvariant();
        var prepared = PrepareResumeTextForSectionParsing(sourceText);
        var lines = prepared
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(80)
            .ToList();
        var skills = SkillKeywords
            .Where(skill => lower.Contains(skill, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(skill => skill, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var projectLines = lines
            .Where(line => line.Contains("project", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("built", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("created", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
        var experienceCount = lines.Count(line =>
            line.Contains("intern", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("engineer", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("developer", StringComparison.OrdinalIgnoreCase) ||
            DateRangeRegex().IsMatch(line));
        var projectCount = Math.Max(
            projectLines.Count,
            lines.Count(line => line.Contains("github", StringComparison.OrdinalIgnoreCase)));
        var structuredData = BuildStructuredData(sourceText, skills, projectLines);
        experienceCount = Math.Max(experienceCount, structuredData.Experience.Count);
        projectCount = Math.Max(projectCount, structuredData.Projects.Count);
        var summary = BuildSummary(lines, skills, experienceCount, projectCount);

        return new ResumeExtraction(
            summary,
            skills,
            projectLines,
            experienceCount,
            projectCount,
            HasEducation(sourceText),
            HasContact(sourceText),
            ExtractLinks(sourceText).Count > 0,
            structuredData);
    }

    private static ResumeStructuredDataDto BuildStructuredData(
        string sourceText,
        IReadOnlyList<string> skills,
        IReadOnlyList<string> projectHighlights)
    {
        var prepared = PrepareResumeTextForSectionParsing(sourceText);
        var lines = prepared
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return new ResumeStructuredDataDto(
            ExtractContact(lines, sourceText),
            ExtractExperience(ExtractSection(lines, "WORK EXPERIENCE", "EXPERIENCE")),
            ExtractEducation(ExtractSection(lines, "EDUCATION")),
            new ResumeSkillGroupsDto(skills, [], []),
            ExtractProjects(ExtractSection(lines, "TECHNICAL PROJECTS", "PROJECTS"), projectHighlights));
    }

    private static string PrepareResumeTextForSectionParsing(string sourceText)
    {
        var normalized = sourceText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("–", "-", StringComparison.Ordinal)
            .Replace("—", "-", StringComparison.Ordinal)
            .Replace("•", "\n• ", StringComparison.Ordinal);
        normalized = KnownResumeHeadingRegex().Replace(normalized, "\n$1\n");
        normalized = EmbeddedResumeEntryStartRegex().Replace(normalized, "\n");
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n\s+", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string ExtractSection(IReadOnlyList<string> lines, params string[] aliases)
    {
        var startIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (aliases.Any(alias => IsResumeHeading(lines[index], alias)))
            {
                startIndex = index + 1;
                break;
            }
        }

        if (startIndex < 0)
        {
            return string.Empty;
        }

        var endIndex = lines.Count;
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (IsKnownResumeHeading(lines[index]))
            {
                endIndex = index;
                break;
            }
        }

        return string.Join('\n', lines.Skip(startIndex).Take(endIndex - startIndex));
    }

    private static bool IsKnownResumeHeading(string line)
    {
        return ResumeHeadingAliases.Any(alias => IsResumeHeading(line, alias));
    }

    private static bool IsResumeHeading(string line, string alias)
    {
        return string.Equals(
            line.Trim().Trim(':').Replace("  ", " ", StringComparison.Ordinal),
            alias,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ResumeContactDto ExtractContact(IReadOnlyList<string> lines, string sourceText)
    {
        var firstHeadingIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (IsKnownResumeHeading(lines[index]))
            {
                firstHeadingIndex = index;
                break;
            }
        }
        var contactLines = lines
            .Take(firstHeadingIndex >= 0 ? firstHeadingIndex : Math.Min(lines.Count, 8))
            .Where(line => !line.StartsWith('•'))
            .ToList();
        var links = ExtractLinks(sourceText)
            .Select(link => new ResumeLinkDto(ResolveLinkLabel(link), link))
            .ToList();
        var email = EmailRegex().Match(sourceText).Value;
        var phone = PhoneRegex().Match(sourceText).Value;
        var name = contactLines
            .Select(ExtractContactNameCandidate)
            .FirstOrDefault(IsUsefulStructuredText) ?? string.Empty;
        var location = contactLines
            .Skip(string.IsNullOrWhiteSpace(name) ? 0 : 1)
            .Prepend(contactLines.FirstOrDefault() ?? string.Empty)
            .SelectMany(line => LocationRegex().Matches(line).Select(match => CleanLocation(match.Value, name)))
            .FirstOrDefault(value => !value.Contains('@')) ?? string.Empty;

        return new ResumeContactDto(name, email, phone, location, links);
    }

    private static IReadOnlyList<ResumeExperienceDto> ExtractExperience(string sectionText)
    {
        var entries = ExtractHeaderAndBulletBlocks(sectionText)
            .Select(block =>
            {
                var dateRange = DateRangeTextRegex().Match(block.Header).Value;
                var leading = dateRange.Length > 0
                    ? block.Header[..block.Header.IndexOf(dateRange, StringComparison.Ordinal)].Trim()
                    : block.Header;
                var rest = dateRange.Length > 0
                    ? block.Header[(block.Header.IndexOf(dateRange, StringComparison.Ordinal) + dateRange.Length)..].Trim()
                    : string.Empty;
                var company = leading;
                var title = ResolveExperienceTitle(rest);
                var titleTrailingText = string.Empty;
                if (TrySplitTitleAtCompany(leading, out var splitTitle, out var splitCompany))
                {
                    title = splitTitle;
                    company = splitCompany;
                }
                else if (string.IsNullOrWhiteSpace(title))
                {
                    var titleMatch = ExperienceTitleRegex()
                        .Matches(leading)
                        .Select(match => match.Value.Trim())
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(titleMatch))
                    {
                        title = titleMatch;
                        var titleIndex = leading.IndexOf(titleMatch, StringComparison.OrdinalIgnoreCase);
                        var beforeTitle = leading[..titleIndex].Trim();
                        var afterTitle = leading[(titleIndex + titleMatch.Length)..].Trim();
                        company = string.IsNullOrWhiteSpace(beforeTitle) ? afterTitle : beforeTitle;
                        titleTrailingText = afterTitle;
                    }
                }
                var location = ResolveLocation(string.Join(" ", [rest, titleTrailingText]));

                return new ResumeExperienceDto(
                    CleanStructuredText(company, 120),
                    CleanStructuredText(title, 120),
                    CleanStructuredText(location, 80),
                    CleanStructuredText(dateRange, 80),
                    block.Bullets.Select(bullet => CleanStructuredText(bullet, 320)).Where(IsUsefulStructuredText).Take(5).ToList());
            })
            .Where(entry => IsUsefulStructuredText(entry.Company) || entry.Bullets.Count > 0)
            .Take(8)
            .ToList();

        return entries;
    }

    private static string ExtractContactNameCandidate(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var cleaned = EmailRegex().Replace(line, " ");
        cleaned = PhoneRegex().Replace(cleaned, " ");
        cleaned = Regex.Replace(cleaned, @"\b(?:Portfolio|GitHub|LinkedIn|US Citizen)\b", " ", RegexOptions.IgnoreCase);
        var firstSegment = cleaned
            .Split(['|', '#', '•'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? cleaned;
        firstSegment = Regex.Replace(firstSegment, @"\s+", " ").Trim();
        if (firstSegment.Length is < 2 or > 100)
        {
            return string.Empty;
        }

        var locationMatch = LocationRegex().Match(firstSegment);
        if (locationMatch.Success && locationMatch.Index > 0)
        {
            firstSegment = firstSegment[..locationMatch.Index].Trim();
        }

        var words = firstSegment
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.All(character => char.IsLetter(character) || character is '-' or '\''))
            .ToList();
        if (words.Count >= 2)
        {
            return CleanStructuredText(string.Join(' ', words.Take(2)), 80);
        }

        return words.Count == 1 ? CleanStructuredText(words[0], 80) : string.Empty;
    }

    private static string CleanLocation(string value, string? name = null)
    {
        var cleaned = CleanStructuredText(value, 120);
        if (!string.IsNullOrWhiteSpace(name) &&
            cleaned.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[name.Length..].Trim();
        }

        var titleMatch = ExperienceTitleRegex().Match(cleaned);
        if (titleMatch.Success && titleMatch.Index == 0)
        {
            cleaned = cleaned[titleMatch.Length..].Trim();
        }

        var commaIndex = cleaned.IndexOf(',');
        if (commaIndex > 0)
        {
            var beforeComma = cleaned[..commaIndex].Trim();
            var state = cleaned[(commaIndex + 1)..].Trim();
            var cityWords = beforeComma
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(3);
            cleaned = $"{string.Join(' ', cityWords)}, {state}";
        }

        return cleaned.Trim(' ', ',', '-', '|');
    }

    private static IReadOnlyList<ResumeEducationDto> ExtractEducation(string sectionText)
    {
        var compact = CleanStructuredText(sectionText, 1200);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return [];
        }

        var schoolMatches = SchoolRegex().Matches(compact).Cast<Match>().ToList();
        if (schoolMatches.Count == 0)
        {
            return [BuildEducationEntry(FirstSentence(compact, 120), compact)];
        }

        return schoolMatches
            .Select((match, index) =>
            {
                var nextStart = index + 1 < schoolMatches.Count
                    ? schoolMatches[index + 1].Index
                    : compact.Length;
                var segment = compact[match.Index..nextStart];
                return BuildEducationEntry(match.Value.Trim(), segment);
            })
            .Where(entry => IsUsefulStructuredText(entry.School) ||
                            IsUsefulStructuredText(entry.Degree) ||
                            IsUsefulStructuredText(entry.Field))
            .Take(12)
            .ToList();
    }

    private static ResumeEducationDto BuildEducationEntry(string school, string segment)
    {
        var degree = DegreeRegex().Matches(segment)
            .Select(match => match.Value.Trim())
            .FirstOrDefault() ?? string.Empty;
        var field = FieldOfStudyRegex().Matches(segment)
            .Select(match => match.Value.Trim())
            .FirstOrDefault() ?? string.Empty;
        var dateRange = DateRangeTextRegex().Matches(segment)
            .Select(match => match.Value.Trim())
            .FirstOrDefault() ?? string.Empty;
        var gpa = GpaRegex().Matches(segment)
            .Select(match => match.Value.Trim())
            .FirstOrDefault() ?? string.Empty;

        return new ResumeEducationDto(
            CleanStructuredText(NormalizeSchoolName(school), 140),
            CleanStructuredText(degree, 80),
            CleanStructuredText(field, 120),
            CleanStructuredText(dateRange, 80),
            CleanStructuredText(gpa, 40),
            []);
    }

    private static string NormalizeSchoolName(string school)
    {
        var cutIndex = school.Length;
        foreach (var regex in new[] { DegreeRegex(), DateRangeTextRegex(), GpaRegex() })
        {
            var match = regex.Match(school);
            if (match.Success)
            {
                cutIndex = Math.Min(cutIndex, match.Index);
            }
        }

        return TrailingMonthRegex()
            .Replace(school[..cutIndex], string.Empty)
            .Trim(' ', ',', '-', '|');
    }

    private static IReadOnlyList<ResumeProjectDto> ExtractProjects(
        string sectionText,
        IReadOnlyList<string> projectHighlights)
    {
        var projects = ExtractHeaderAndBulletBlocks(sectionText)
            .Select(block =>
            {
                var name = CleanProjectName(StripTrailingDateRange(block.Header));
                var bullets = block.Bullets
                    .Select(bullet => CleanStructuredText(bullet, 360))
                    .Where(IsUsefulStructuredText)
                    .Take(5)
                    .ToList();
                var description = bullets.FirstOrDefault() ?? string.Empty;

                return new ResumeProjectDto(
                    CleanStructuredText(name, 140),
                    description,
                    ExtractProjectTech(string.Join(" ", [name, .. bullets])),
                    ExtractLinks(string.Join(" ", [name, .. bullets])),
                    bullets);
            })
            .Where(project => IsUsefulStructuredText(project.Name) || project.Bullets.Count > 0)
            .Take(8)
            .ToList();

        if (projects.Count > 0)
        {
            return projects;
        }

        return projectHighlights
            .Select(highlight => CleanStructuredText(highlight, 360))
            .Where(IsUsefulStructuredText)
            .Where(highlight => !LooksLikeWholeResumeBlob(highlight))
            .Take(6)
            .Select((highlight, index) => new ResumeProjectDto(
                $"Project {index + 1}",
                highlight,
                ExtractProjectTech(highlight),
                ExtractLinks(highlight),
                [highlight]))
            .ToList();
    }

    private static IReadOnlyList<(string Header, IReadOnlyList<string> Bullets)> ExtractHeaderAndBulletBlocks(
        string sectionText)
    {
        var normalizedSectionText = DenseResumeEntryStartRegex().Replace(sectionText, "\n");
        var lines = normalizedSectionText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !IsKnownResumeHeading(line))
            .ToList();
        var blocks = new List<(string Header, IReadOnlyList<string> Bullets)>();
        string? currentHeader = null;
        var bullets = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith('•'))
            {
                var bullet = line.TrimStart('•').Trim();
                if (bullet.Length > 0)
                {
                    bullets.Add(bullet);
                }

                continue;
            }

            if (currentHeader is not null)
            {
                blocks.Add((currentHeader, bullets.ToList()));
                bullets.Clear();
            }

            currentHeader = line;
        }

        if (currentHeader is not null)
        {
            blocks.Add((currentHeader, bullets.ToList()));
        }

        return blocks;
    }

    private static string ResolveExperienceTitle(string text)
    {
        var match = ExperienceTitleRegex().Matches(text).Select(match => match.Value.Trim()).FirstOrDefault();
        return match ?? FirstSentence(text, 120);
    }

    private static string ResolveLocation(string text)
    {
        return LocationRegex().Matches(text)
            .Select(match => CleanLocation(match.Value))
            .FirstOrDefault() ?? string.Empty;
    }

    private static string StripTrailingDateRange(string text)
    {
        return CleanStructuredText(ResumeDateTextRegex().Replace(text, string.Empty), 160);
    }

    private static string CleanProjectName(string text)
    {
        var segments = text
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(segment =>
                !segment.Equals("§", StringComparison.OrdinalIgnoreCase) &&
                !segment.Equals("#", StringComparison.OrdinalIgnoreCase) &&
                !segment.Contains("live demo", StringComparison.OrdinalIgnoreCase) &&
                !segment.Contains("github", StringComparison.OrdinalIgnoreCase) &&
                !segment.Contains("linkedin", StringComparison.OrdinalIgnoreCase) &&
                !segment.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cleaned = segments.Count > 0 ? segments[0] : text;
        cleaned = cleaned.Replace("§", string.Empty, StringComparison.Ordinal);
        return CleanStructuredText(cleaned, 140).Trim(' ', '|', '-', '#');
    }

    private static bool TrySplitTitleAtCompany(
        string value,
        out string title,
        out string company)
    {
        var parts = value.Split(" @ ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && ExperienceTitleRegex().IsMatch(parts[0]))
        {
            title = parts[0];
            company = parts[1];
            return true;
        }

        title = string.Empty;
        company = value;
        return false;
    }

    private static string FirstSentence(string text, int maxLength)
    {
        var sentence = text.Split(['.', '|', '•'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? text;
        return CleanStructuredText(sentence, maxLength);
    }

    private static string CleanStructuredText(string? value, int maxLength)
    {
        var cleaned = value?.Trim() ?? string.Empty;
        cleaned = Regex.Replace(cleaned, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", " ");
        cleaned = Regex.Replace(cleaned, @"(?<=\p{L})-\s+(?=\p{Ll})", "");
        cleaned = Regex.Replace(cleaned, @"(?:^|\s)(?:§|ï|#)\s*(?=(?:Portfolio|GitHub|LinkedIn)\b)", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        return cleaned[..maxLength].TrimEnd() + "...";
    }

    private static bool IsUsefulStructuredText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length > 1;
    }

    private static bool LooksLikeWholeResumeBlob(string value)
    {
        return value.Length > 700 ||
               (value.Contains("EDUCATION", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("WORK EXPERIENCE", StringComparison.OrdinalIgnoreCase)) ||
               (value.Contains("SKILLS", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("PROJECT", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("EXPERIENCE", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ExtractProjectTech(string text)
    {
        var lower = text.ToLowerInvariant();
        return SkillKeywords
            .Where(skill => lower.Contains(skill, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static string ResolveLinkLabel(string url)
    {
        if (url.Contains("github", StringComparison.OrdinalIgnoreCase)) return "github";
        if (url.Contains("linkedin", StringComparison.OrdinalIgnoreCase)) return "linkedin";
        if (url.Contains("portfolio", StringComparison.OrdinalIgnoreCase)) return "portfolio";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "link";
    }

    private static bool HasEducation(string sourceText)
    {
        return sourceText.Contains("education", StringComparison.OrdinalIgnoreCase) ||
               sourceText.Contains("university", StringComparison.OrdinalIgnoreCase) ||
               sourceText.Contains("bachelor", StringComparison.OrdinalIgnoreCase) ||
               sourceText.Contains("master", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasContact(string sourceText)
    {
        return EmailRegex().IsMatch(sourceText) || PhoneRegex().IsMatch(sourceText);
    }

    private static IReadOnlyList<string> ExtractLinks(string sourceText)
    {
        return LinkRegex()
            .Matches(sourceText)
            .Select(match => match.Value.TrimEnd('.', ',', ')', ']'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }
}

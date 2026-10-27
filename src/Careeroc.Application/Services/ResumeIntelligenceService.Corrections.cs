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
    private static ResumeExtraction ApplyCorrections(
        ResumeExtraction extraction,
        IEnumerable<ResumeCorrection> corrections)
    {
        var summary = extraction.Summary;
        var skills = extraction.Skills.ToList();
        var projectHighlights = extraction.ProjectHighlights.ToList();
        var experienceCount = extraction.ExperienceCount;
        var projectCount = extraction.ProjectCount;
        var hasEducation = extraction.HasEducation;
        var hasContact = extraction.HasContact;
        var hasLinks = extraction.HasLinks;

        foreach (var correction in corrections.OrderBy(correction => correction.CreatedAtUtc))
        {
            var fieldPath = correction.FieldPath.Trim();
            var value = correction.CorrectedValue.Trim();
            var root = NormalizeCorrectionFieldRoot(fieldPath);

            switch (root)
            {
                case "summary":
                    summary = value;
                    break;
                case "skills":
                    skills = ApplyListCorrection(skills, fieldPath, value);
                    break;
                case "projectHighlights":
                    projectHighlights = ApplyListCorrection(projectHighlights, fieldPath, value);
                    projectCount = Math.Max(projectCount, projectHighlights.Count);
                    break;
                case "experienceCount":
                    experienceCount = ParseCorrectionCount(value, experienceCount);
                    break;
                case "projectCount":
                    projectCount = ParseCorrectionCount(value, projectCount);
                    break;
                case "education":
                    hasEducation = ParseCorrectionBoolean(value, hasEducation);
                    break;
                case "contact":
                    hasContact = ParseCorrectionBoolean(value, hasContact);
                    break;
                case "links":
                    hasLinks = ParseCorrectionBoolean(value, hasLinks);
                    break;
            }
        }

        return extraction with
        {
            Summary = summary,
            Skills = skills,
            ProjectHighlights = projectHighlights,
            ExperienceCount = experienceCount,
            ProjectCount = projectCount,
            HasEducation = hasEducation,
            HasContact = hasContact,
            HasLinks = hasLinks
        };
    }

    private static string NormalizeCorrectionFieldRoot(string fieldPath)
    {
        var bracketIndex = fieldPath.IndexOf('[');
        return bracketIndex > 0 ? fieldPath[..bracketIndex] : fieldPath;
    }

    private static List<string> ApplyListCorrection(
        IReadOnlyList<string> current,
        string fieldPath,
        string correctedValue)
    {
        var next = current.ToList();
        var index = TryParseListIndex(fieldPath);
        if (index is not null)
        {
            while (next.Count <= index.Value)
            {
                next.Add(string.Empty);
            }

            next[index.Value] = correctedValue;
            return CleanCorrectionList(next);
        }

        return CleanCorrectionList(SplitCorrectionList(correctedValue));
    }

    private static int? TryParseListIndex(string fieldPath)
    {
        var start = fieldPath.IndexOf('[');
        var end = fieldPath.IndexOf(']', start + 1);
        if (start < 0 || end <= start)
        {
            return null;
        }

        return int.TryParse(fieldPath[(start + 1)..end], out var index) && index >= 0
            ? index
            : null;
    }

    private static IReadOnlyList<string> SplitCorrectionList(string value)
    {
        return value
            .Split(['\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<string> CleanCorrectionList(IEnumerable<string?>? values)
    {
        return (values ?? [])
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
    }

    private static int ParseCorrectionCount(string value, int fallback)
    {
        return int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, 0, 50)
            : fallback;
    }

    private static bool ParseCorrectionBoolean(string value, bool fallback)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" or "present" or "detected" => true,
            "false" or "no" or "n" or "0" or "missing" or "absent" => false,
            _ => fallback
        };
    }

    private static NormalizedCorrection NormalizeCorrection(CorrectResumeFieldRequest request)
    {
        var fieldPath = request.FieldPath.Trim();
        if (fieldPath.Length is 0 or > 240 || !FieldPathRegex().IsMatch(fieldPath))
        {
            throw new InvalidOperationException("Correction field path is invalid.");
        }

        var correctedValue = request.CorrectedValue.Trim();
        if (correctedValue.Length is 0 or > 2000)
        {
            throw new InvalidOperationException("Corrected value must be between 1 and 2,000 characters.");
        }

        var previousValue = NormalizeOptionalCorrectionValue(request.PreviousValue, 2000, "Previous value");
        var note = NormalizeOptionalCorrectionValue(request.Note, 1000, "Correction note");
        return new NormalizedCorrection(fieldPath, previousValue, correctedValue, note);
    }

    private static string? NormalizeOptionalCorrectionValue(string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

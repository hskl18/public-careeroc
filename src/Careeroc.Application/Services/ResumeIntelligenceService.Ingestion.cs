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

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
    private static string NormalizeSourceText(string value)
    {
        var normalized = CleanSourceText(value);
        if (normalized.Length < 80)
        {
            throw new InvalidOperationException("Paste at least 80 characters of resume text.");
        }

        if (normalized.Length > 100_000)
        {
            throw new InvalidOperationException("Resume text must be 100,000 characters or fewer.");
        }

        return normalized;
    }

    private static string CleanSourceText(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("�", "", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", " ");
        normalized = Regex.Replace(normalized, @"(?<=\p{L})-\s+(?=\p{Ll})", "");
        normalized = Regex.Replace(normalized, @"(?:^|\s)(?:§|ï|#)\s*(?=(?:Portfolio|GitHub|LinkedIn)\b)", " ");
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n\s+", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string NormalizeTitle(string? value, string sourceText)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed.Length <= 240 ? trimmed : trimmed[..240];
        }

        var firstLine = sourceText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine)
            ? "Resume"
            : firstLine.Length <= 80
                ? firstLine
                : "Resume";
    }

    private static string NormalizeSourceKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "text" or "pdf" or "image"
            ? normalized
            : "text";
    }

}

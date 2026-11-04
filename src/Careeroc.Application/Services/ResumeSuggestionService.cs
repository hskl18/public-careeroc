using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ResumeSuggestionService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public ResumeSuggestionService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ResumeSuggestionDto>?> GetSuggestionsAsync(
        Guid resumeDocumentId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var version = await GetLatestVersionAsync(resumeDocumentId, userEmail, cancellationToken);
        return version is null ? null : BuildSuggestions(version);
    }

    public async Task<bool?> AcceptSuggestionAsync(
        Guid resumeDocumentId,
        Guid suggestionId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var version = await GetLatestVersionAsync(resumeDocumentId, userEmail, cancellationToken, tracking: true);
        if (version is null)
        {
            return null;
        }

        var suggestion = BuildSuggestions(version).FirstOrDefault(candidate => candidate.Id == suggestionId);
        if (suggestion is null)
        {
            return false;
        }

        var nowUtc = _clock.UtcNow;
        var updatedStructuredData = ReplaceFirst(
            version.StructuredDataJson,
            suggestion.OriginalText,
            suggestion.SuggestedText);
        var updatedSourceText = ReplaceFirst(
            version.ResumeDocument.SourceText,
            suggestion.OriginalText,
            suggestion.SuggestedText);
        var newVersion = new ResumeVersion
        {
            ResumeDocumentId = version.ResumeDocumentId,
            Summary = ReplaceFirst(version.Summary, suggestion.OriginalText, suggestion.SuggestedText),
            SkillsJson = version.SkillsJson,
            ProjectHighlightsJson = version.ProjectHighlightsJson,
            StructuredDataJson = updatedStructuredData,
            ExtractionMetadataJson = version.ExtractionMetadataJson,
            EvaluationRunId = version.EvaluationRunId,
            ExperienceCount = version.ExperienceCount,
            ProjectCount = version.ProjectCount,
            CreatedAtUtc = nowUtc
        };
        version.ResumeDocument.SourceText = updatedSourceText;
        version.ResumeDocument.UpdatedAtUtc = nowUtc;
        _dbContext.ResumeVersions.Add(newVersion);
        _dbContext.ResumeCorrections.Add(
            new ResumeCorrection
            {
                ResumeVersion = newVersion,
                FieldPath = suggestion.TargetSection,
                PreviousValue = suggestion.OriginalText,
                CorrectedValue = suggestion.SuggestedText,
                Note = suggestion.Rationale,
                CreatedAtUtc = nowUtc
            });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> DeclineSuggestionAsync(
        Guid resumeDocumentId,
        Guid suggestionId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var version = await GetLatestVersionAsync(resumeDocumentId, userEmail, cancellationToken);
        if (version is null)
        {
            return null;
        }

        return BuildSuggestions(version).Any(candidate => candidate.Id == suggestionId);
    }

    private async Task<ResumeVersion?> GetLatestVersionAsync(
        Guid resumeDocumentId,
        string? userEmail,
        CancellationToken cancellationToken,
        bool tracking = false)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        IQueryable<ResumeVersion> query = _dbContext.ResumeVersions
            .Where(version =>
                version.ResumeDocumentId == resumeDocumentId &&
                version.ResumeDocument.User.Email == resolvedUserEmail)
            .Include(version => version.ResumeDocument)
            .OrderByDescending(version => version.CreatedAtUtc);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static IReadOnlyList<ResumeSuggestionDto> BuildSuggestions(ResumeVersion version)
    {
        var suggestions = new List<ResumeSuggestionDto>();
        if (version.StructuredDataJson.Contains("Not detected", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(BuildSuggestion(
                "add",
                "contact",
                "Not detected",
                "Add the missing contact or education detail if it exists on the source resume.",
                "A parsed field is missing and should be confirmed before targeted resume generation.",
                version.Id));
        }

        var longBullet = ExtractLongBullet(version.StructuredDataJson);
        if (!string.IsNullOrWhiteSpace(longBullet))
        {
            suggestions.Add(BuildSuggestion(
                "rewrite",
                "experience",
                longBullet,
                TrimSentence(longBullet, 180),
                "This bullet is long enough to hurt scanability; tighten it before generating targeted versions.",
                version.Id));
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add(BuildSuggestion(
                "add",
                "projects",
                string.Empty,
                "Add one metric or outcome to the strongest project.",
                "Deterministic review found structure but can still improve impact evidence.",
                version.Id));
        }

        return suggestions.Take(5).ToList();
    }

    private static ResumeSuggestionDto BuildSuggestion(
        string kind,
        string targetSection,
        string originalText,
        string suggestedText,
        string rationale,
        Guid resumeVersionId)
    {
        return new ResumeSuggestionDto(
            BuildStableId($"{resumeVersionId}:{kind}:{targetSection}:{originalText}:{suggestedText}"),
            kind,
            targetSection,
            originalText,
            suggestedText,
            rationale,
            [$"resume_version:{resumeVersionId}"]);
    }

    private static Guid BuildStableId(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes[..16]);
    }

    private static string? ExtractLongBullet(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FindLongString(document.RootElement);
    }

    private static string? FindLongString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return value is { Length: > 220 } ? value : null;
        }

        var children = element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(property => property.Value),
            JsonValueKind.Array => element.EnumerateArray(),
            _ => []
        };

        foreach (var child in children)
        {
            var found = FindLongString(child);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return null;
    }

    private static string TrimSentence(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : $"{value[..Math.Max(0, maxLength - 1)].Trim()}...";
    }

    private static string ReplaceFirst(string value, string originalText, string suggestedText)
    {
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return value;
        }

        var index = value.IndexOf(originalText, StringComparison.Ordinal);
        return index < 0
            ? value
            : string.Concat(value.AsSpan(0, index), suggestedText, value.AsSpan(index + originalText.Length));
    }
}

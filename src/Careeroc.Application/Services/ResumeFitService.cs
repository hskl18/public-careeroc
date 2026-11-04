using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ResumeFitService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IResumeFitEnricher _resumeFitEnricher;

    public ResumeFitService(
        ICareerocDbContext dbContext,
        IClock clock,
        IResumeFitEnricher? resumeFitEnricher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _resumeFitEnricher = resumeFitEnricher ?? NullResumeFitEnricher.Instance;
    }

    public async Task<ResumeFitDto?> GetFitAsync(
        Guid jobApplicationId,
        string? userEmail,
        Guid? resumeVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var application = await _dbContext.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == jobApplicationId &&
                             candidate.User.Email == resolvedUserEmail,
                cancellationToken);
        if (application is null)
        {
            return null;
        }

        var resumeVersionQuery = _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(version => version.ResumeDocument.User.Email == resolvedUserEmail);
        resumeVersionQuery = resumeVersionId.HasValue
            ? resumeVersionQuery.Where(version => version.Id == resumeVersionId.Value)
            : resumeVersionQuery.OrderByDescending(version => version.CreatedAtUtc);

        var resumeVersion = await resumeVersionQuery
            .Include(version => version.ResumeDocument)
            .FirstOrDefaultAsync(cancellationToken);
        if (resumeVersion is null)
        {
            return new ResumeFitDto(
                application.Id,
                null,
                0,
                [],
                ["No resume version is available to compare against this application."],
                "Upload or confirm a resume before scoring role fit.");
        }

        var cached = await _dbContext.ApplicationResumeFits
            .AsNoTracking()
            .FirstOrDefaultAsync(
                fit =>
                    fit.JobApplicationId == application.Id &&
                    fit.ResumeVersionId == resumeVersion.Id &&
                    fit.UpdatedAtUtc >= application.LastActivityAtUtc &&
                    fit.UpdatedAtUtc >= resumeVersion.CreatedAtUtc,
                cancellationToken);
        if (cached is not null)
        {
            return MapCachedFit(cached);
        }

        var resumeSkills = ExtractResumeKeywords(resumeVersion);
        var roleKeywords = ExtractRoleKeywords(
            string.Join(
                ' ',
                application.Role,
                application.RoleCanonical,
                application.Company,
                application.Location,
                application.SourceSystem,
                application.Source));
        var matched = roleKeywords
            .Where(keyword => resumeSkills.Contains(keyword))
            .OrderBy(keyword => keyword)
            .ToList();
        var missing = roleKeywords
            .Where(keyword => !resumeSkills.Contains(keyword))
            .OrderBy(keyword => keyword)
            .Take(8)
            .ToList();

        var matchScore = roleKeywords.Count == 0
            ? 50
            : Math.Clamp(50 + (matched.Count * 50 / Math.Max(roleKeywords.Count, 1)) - missing.Count * 3, 0, 100);
        var strengths = matched.Count == 0
            ? ["Resume has a structured baseline but no role-keyword overlap was detected."]
            : matched.Select(keyword => $"Resume mentions {keyword}.").Take(6).ToList();
        var gaps = missing.Count == 0
            ? ["No obvious deterministic keyword gap was detected."]
            : missing.Select(keyword => $"Add evidence for {keyword} if relevant.").ToList();

        var response = new ResumeFitDto(
            application.Id,
            resumeVersion.Id,
            matchScore,
            strengths,
            gaps,
            $"Deterministic fit based on {matched.Count}/{Math.Max(roleKeywords.Count, 1)} role keywords found in the latest resume.");
        var enriched = await _resumeFitEnricher.EnrichAsync(response, cancellationToken);
        await UpsertCacheAsync(application.UserId, enriched, cancellationToken);
        return enriched;
    }

    private async Task UpsertCacheAsync(
        Guid userId,
        ResumeFitDto response,
        CancellationToken cancellationToken)
    {
        if (!response.ResumeVersionId.HasValue)
        {
            return;
        }

        var nowUtc = _clock.UtcNow;
        var cached = await _dbContext.ApplicationResumeFits
            .FirstOrDefaultAsync(
                fit =>
                    fit.JobApplicationId == response.JobApplicationId &&
                    fit.ResumeVersionId == response.ResumeVersionId.Value,
                cancellationToken);
        if (cached is null)
        {
            cached = new ApplicationResumeFit
            {
                UserId = userId,
                JobApplicationId = response.JobApplicationId,
                ResumeVersionId = response.ResumeVersionId.Value,
                CreatedAtUtc = nowUtc
            };
            _dbContext.ApplicationResumeFits.Add(cached);
        }

        cached.MatchScore = response.MatchScore;
        cached.StrengthsJson = JsonSerializer.Serialize(response.Strengths, JsonOptions);
        cached.GapsJson = JsonSerializer.Serialize(response.Gaps, JsonOptions);
        cached.Rationale = response.Rationale;
        cached.UpdatedAtUtc = nowUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ResumeFitDto MapCachedFit(ApplicationResumeFit fit)
    {
        return new ResumeFitDto(
            fit.JobApplicationId,
            fit.ResumeVersionId,
            fit.MatchScore,
            DeserializeList(fit.StrengthsJson),
            DeserializeList(fit.GapsJson),
            fit.Rationale);
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HashSet<string> ExtractResumeKeywords(Careeroc.Domain.Entities.ResumeVersion version)
    {
        var text = string.Join(
            ' ',
            version.Summary,
            version.SkillsJson,
            version.ProjectHighlightsJson,
            version.StructuredDataJson,
            version.ResumeDocument.SourceText);
        return ExtractRoleKeywords(text);
    }

    private static HashSet<string> ExtractRoleKeywords(string? text)
    {
        var normalized = (text ?? string.Empty).ToLowerInvariant();
        var keywords = new[]
        {
            "typescript", "javascript", "react", "next.js", "node", "python", "java", "c#", ".net",
            "sql", "postgres", "aws", "docker", "kubernetes", "llm", "rag", "machine learning",
            "data", "backend", "frontend", "full-stack", "cloud", "serverless", "api", "etl",
            "recommendation", "security", "salesforce"
        };

        return keywords
            .Where(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

}

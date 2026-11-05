using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResumeMutationLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SkillKeywords =
    [
        "typescript",
        "javascript",
        "react",
        "next.js",
        "node",
        "python",
        "c#",
        ".net",
        "java",
        "sql",
        "postgres",
        "aws",
        "docker",
        "kubernetes",
        "llm",
        "rag",
        "openai",
        "machine learning",
        "data",
        "api"
    ];
    private static readonly string[] ResumeHeadingAliases =
    [
        "CONTACT",
        "SUMMARY",
        "EDUCATION",
        "WORK EXPERIENCE",
        "EXPERIENCE",
        "PROFESSIONAL EXPERIENCE",
        "TECHNICAL PROJECTS",
        "PROJECTS",
        "SKILLS",
        "TECHNICAL SKILLS",
        "CERTIFICATIONS",
        "PUBLICATIONS",
        "AWARDS"
    ];

    private readonly ICareerocDbContext _dbContext;
    private readonly ResumeMailboxContextBuilder _resumeMailboxContextBuilder;
    private readonly IClock _clock;

    public ResumeIntelligenceService(
        ICareerocDbContext dbContext,
        IClock clock,
        ResumeMailboxContextBuilder? resumeMailboxContextBuilder = null)
    {
        _dbContext = dbContext;
        _resumeMailboxContextBuilder = resumeMailboxContextBuilder ?? new ResumeMailboxContextBuilder(dbContext, clock);
        _clock = clock;
    }

    public async Task<ResumeWorkspaceDto> GetWorkspaceAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, cancellationToken);
        var documents = await _dbContext.ResumeDocuments
            .AsNoTracking()
            .Where(document => document.UserId == user.Id)
            .Include(document => document.Versions)
                .ThenInclude(version => version.Evaluation)
            .OrderByDescending(document => document.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var memoryProposals = await LoadResumeMemoryProposalsAsync(
            user.Id,
            documents,
            cancellationToken);
        return new ResumeWorkspaceDto(
            documents
                .Select(
                    document => MapDocument(
                        document,
                        memoryProposals.GetValueOrDefault(document.Id) ?? []))
                .ToList(),
            BuildReadiness(documents));
    }

    public async Task<ResumeWorkspaceDto> CreateResumeAsync(
        CreateResumeRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = await AcquireResumeMutationLockAsync(resolvedUserEmail, cancellationToken);
        try
        {
            var user = await GetOrCreateUserAsync(resolvedUserEmail, cancellationToken);
            var sourceText = NormalizeSourceText(request.SourceText);
            var title = NormalizeTitle(request.Title, sourceText);
            var sourceKind = NormalizeSourceKind(request.SourceKind);
            var sourceFileName = NormalizeOptionalSourceFileName(request.SourceFileName);
            var nowUtc = _clock.UtcNow;
            var document = new ResumeDocument
            {
                UserId = user.Id,
                User = user,
                Title = title,
                SourceKind = sourceKind,
                SourceFileName = sourceFileName,
                SourceText = sourceText,
                SourceLinksJson = SerializeList(ExtractLinks(sourceText)),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            var extraction = ExtractResume(sourceText);
            var context = await LoadEvaluationContextAsync(user.Id, cancellationToken);
            var evaluationResult = EvaluateResume(extraction, context);
            var structuredData = BuildStructuredData(sourceText, extraction.Skills, extraction.ProjectHighlights);
            var extractionMetadata = BuildExtractionMetadata(
                sourceKind,
                sourceFileName,
                sourceText,
                structuredData,
                "initial_import",
                nowUtc,
                ResumePromptVersions.DeterministicExtraction);
            var version = new ResumeVersion
            {
                ResumeDocumentId = document.Id,
                ResumeDocument = document,
                Summary = extraction.Summary,
                SkillsJson = SerializeList(extraction.Skills),
                ProjectHighlightsJson = SerializeList(extraction.ProjectHighlights),
                StructuredDataJson = SerializeStructuredData(structuredData),
                ExtractionMetadataJson = JsonSerializer.Serialize(extractionMetadata, JsonOptions),
                ExperienceCount = extraction.ExperienceCount,
                ProjectCount = extraction.ProjectCount,
                CreatedAtUtc = nowUtc
            };
            var evaluation = new ResumeEvaluation
            {
                ResumeVersionId = version.Id,
                ResumeVersion = version,
                Score = evaluationResult.Score,
                Headline = evaluationResult.Headline,
                Verdict = evaluationResult.Verdict,
                StrengthsJson = SerializeList(evaluationResult.Strengths),
                GapsJson = SerializeList(evaluationResult.Gaps),
                NextActionsJson = SerializeList(evaluationResult.NextActions),
                SectionReviewsJson = JsonSerializer.Serialize(evaluationResult.SectionReviews, JsonOptions),
                EvidenceJson = JsonSerializer.Serialize(evaluationResult.Evidence, JsonOptions),
                CreatedAtUtc = nowUtc
            };

            version.Evaluation = evaluation;
            document.Versions.Add(version);
            _dbContext.ResumeDocuments.Add(document);
            await UpsertResumeEvaluationMemoryAsync(
                user.Id,
                title,
                evaluation.Id,
                extraction,
                evaluationResult,
                nowUtc,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return await GetWorkspaceAsync(user.Email, cancellationToken);
        }
        finally
        {
            ReleaseResumeMutationLock(mutationLock);
        }
    }


    private static async Task<SemaphoreSlim> AcquireResumeMutationLockAsync(
        string userEmail,
        CancellationToken cancellationToken)
    {
        var normalizedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = ResumeMutationLocks.GetOrAdd(normalizedUserEmail, _ => new SemaphoreSlim(1, 1));
        await mutationLock.WaitAsync(cancellationToken);
        return mutationLock;
    }

    private static void ReleaseResumeMutationLock(SemaphoreSlim mutationLock)
    {
        mutationLock.Release();
    }
}

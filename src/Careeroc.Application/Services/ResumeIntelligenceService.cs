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
using UglyToad.PdfPig;

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
        "gemma",
        "ollama",
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
    private readonly IResumeEvaluationModel _resumeEvaluationModel;
    private readonly IResumeVisionTextExtractor _resumeVisionTextExtractor;
    private readonly ResumeMailboxContextBuilder _resumeMailboxContextBuilder;
    private readonly IClock _clock;

    public ResumeIntelligenceService(ICareerocDbContext dbContext, IClock clock)
        : this(dbContext, NullResumeEvaluationModel.Instance, NullResumeVisionTextExtractor.Instance, clock)
    {
    }

    public ResumeIntelligenceService(
        ICareerocDbContext dbContext,
        IResumeEvaluationModel resumeEvaluationModel,
        IClock clock)
        : this(dbContext, resumeEvaluationModel, NullResumeVisionTextExtractor.Instance, clock)
    {
    }

    public ResumeIntelligenceService(
        ICareerocDbContext dbContext,
        IResumeEvaluationModel resumeEvaluationModel,
        IResumeVisionTextExtractor resumeVisionTextExtractor,
        IClock clock,
        ResumeMailboxContextBuilder? resumeMailboxContextBuilder = null)
    {
        _dbContext = dbContext;
        _resumeEvaluationModel = resumeEvaluationModel;
        _resumeVisionTextExtractor = resumeVisionTextExtractor;
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
            .Include(document => document.Versions)
                .ThenInclude(version => version.Corrections)
            .OrderByDescending(document => document.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var modelRuns = await LoadLatestResumeModelRunsAsync(documents, cancellationToken);
        var memoryProposals = await LoadResumeMemoryProposalsAsync(
            user.Id,
            documents,
            cancellationToken);
        return new ResumeWorkspaceDto(
            documents
                .Select(
                    document => MapDocument(
                        document,
                        modelRuns,
                        memoryProposals.GetValueOrDefault(document.Id) ?? []))
                .ToList(),
            BuildReadiness(documents));
    }

    public async Task<ResumeWorkspaceDto> CreateResumeAsync(
        CreateResumeRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        return await CreateResumeAsync(request, userEmail, null, cancellationToken);
    }

    private async Task<ResumeWorkspaceDto> CreateResumeAsync(
        CreateResumeRequest request,
        string? userEmail,
        Guid? runIdOverride,
        CancellationToken cancellationToken,
        ResumeOcrImportMetadata? ocrMetadata = null)
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
            var runId = runIdOverride ?? Guid.NewGuid();
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
            var modelEnhanced = await EvaluateResumeWithModelAsync(
                sourceText,
                extraction,
                context,
                [],
                user.Email,
                runId,
                cancellationToken);
            extraction = modelEnhanced.Extraction;
            var evaluationResult = modelEnhanced.Evaluation;
            var structuredData = modelEnhanced.StructuredData ??
                                 BuildStructuredData(sourceText, extraction.Skills, extraction.ProjectHighlights);
            var extractionMetadata = BuildExtractionMetadata(
                sourceKind,
                sourceFileName,
                sourceText,
                structuredData,
                "initial_import",
                nowUtc,
                modelEnhanced.StructuredData is null
                    ? ResumePromptVersions.DeterministicExtraction
                    : modelEnhanced.PromptVersion,
                ocrMetadata);
            var version = new ResumeVersion
            {
                ResumeDocumentId = document.Id,
                ResumeDocument = document,
                Summary = extraction.Summary,
                SkillsJson = SerializeList(extraction.Skills),
                ProjectHighlightsJson = SerializeList(extraction.ProjectHighlights),
                StructuredDataJson = SerializeStructuredData(structuredData),
                ExtractionMetadataJson = JsonSerializer.Serialize(extractionMetadata, JsonOptions),
                EvaluationRunId = runId.ToString("N"),
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

    public async Task<ResumeWorkspaceDto> ImportResumeFileAsync(
        ImportResumeFileRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var fileName = NormalizeFileName(request.FileName);
        var bytes = DecodeBase64Content(request.Base64Content);
        var sourceKind = ResolveSourceKind(fileName, request.ContentType);
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var runId = Guid.NewGuid();
        var sourceImport = sourceKind switch
        {
            "pdf" => await ExtractPdfTextOrVisionAsync(bytes, fileName, resolvedUserEmail, runId, cancellationToken),
            "text" => new ResumeSourceImportResult(
                DecodeText(bytes),
                new ResumeOcrImportMetadata("not_required")),
            "image" => await ExtractVisionTextAsync(
                bytes,
                NormalizeImageContentType(request.ContentType, fileName),
                fileName,
                "image",
                resolvedUserEmail,
                runId,
                cancellationToken),
            _ => throw new InvalidOperationException("Only PDF, image, and plain text resume imports are supported right now.")
        };
        var sourceText = sourceImport.SourceText;
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : request.Title;

        return await CreateResumeAsync(
            new CreateResumeRequest(title, sourceKind, sourceText, fileName),
            resolvedUserEmail,
            runId,
            cancellationToken,
            sourceImport.OcrMetadata);
    }

    public async Task<ResumeWorkspaceDto?> ReextractResumeAsync(
        Guid resumeDocumentId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = await AcquireResumeMutationLockAsync(resolvedUserEmail, cancellationToken);
        try
        {
            var user = await GetOrCreateUserAsync(resolvedUserEmail, cancellationToken);
            var document = await _dbContext.ResumeDocuments
                .Include(candidate => candidate.Versions)
                    .ThenInclude(version => version.Corrections)
                .Include(candidate => candidate.Versions)
                    .ThenInclude(version => version.Evaluation)
                .Where(candidate => candidate.Id == resumeDocumentId && candidate.UserId == user.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (document is null)
            {
                return null;
            }

            var latestVersion = document.Versions
                .OrderByDescending(version => version.CreatedAtUtc)
                .FirstOrDefault();
            var corrections = latestVersion?.Corrections ?? [];
            var nowUtc = _clock.UtcNow;
            var runId = Guid.NewGuid();
            var extraction = ApplyCorrections(ExtractResume(document.SourceText), corrections);
            var context = await LoadEvaluationContextAsync(user.Id, cancellationToken);
            var modelEnhanced = await EvaluateResumeWithModelAsync(
                document.SourceText,
                extraction,
                context,
                corrections,
                user.Email,
                runId,
                cancellationToken);
            extraction = modelEnhanced.Extraction;
            var evaluationResult = modelEnhanced.Evaluation;
            var structuredData = modelEnhanced.StructuredData ??
                                 BuildStructuredData(document.SourceText, extraction.Skills, extraction.ProjectHighlights);
            structuredData = ApplyStructuredCorrections(structuredData, corrections);
            var extractionMetadata = BuildExtractionMetadata(
                document.SourceKind,
                document.SourceFileName,
                document.SourceText,
                structuredData,
                "reextract",
                nowUtc,
                modelEnhanced.StructuredData is null
                    ? ResumePromptVersions.DeterministicExtraction
                    : modelEnhanced.PromptVersion);

            var version = new ResumeVersion
            {
                ResumeDocumentId = document.Id,
                ResumeDocument = document,
                Summary = extraction.Summary,
                SkillsJson = SerializeList(extraction.Skills),
                ProjectHighlightsJson = SerializeList(extraction.ProjectHighlights),
                StructuredDataJson = SerializeStructuredData(structuredData),
                ExtractionMetadataJson = JsonSerializer.Serialize(extractionMetadata, JsonOptions),
                EvaluationRunId = runId.ToString("N"),
                ExperienceCount = Math.Max(extraction.ExperienceCount, structuredData.Experience.Count),
                ProjectCount = Math.Max(extraction.ProjectCount, structuredData.Projects.Count),
                CreatedAtUtc = nowUtc,
                Evaluation = new ResumeEvaluation
                {
                    Score = evaluationResult.Score,
                    Headline = evaluationResult.Headline,
                    Verdict = evaluationResult.Verdict,
                    StrengthsJson = SerializeList(evaluationResult.Strengths),
                    GapsJson = SerializeList(evaluationResult.Gaps),
                    NextActionsJson = SerializeList(evaluationResult.NextActions),
                    SectionReviewsJson = JsonSerializer.Serialize(evaluationResult.SectionReviews, JsonOptions),
                    EvidenceJson = JsonSerializer.Serialize(evaluationResult.Evidence, JsonOptions),
                    CreatedAtUtc = nowUtc
                }
            };

            // Added through the DbSet: a new version found only through a tracked document's
            // collection has a preset Guid key, so EF would issue an UPDATE for a row that does not exist.
            _dbContext.ResumeVersions.Add(version);
            document.UpdatedAtUtc = nowUtc;
            await UpsertResumeEvaluationMemoryAsync(
                user.Id,
                document.Title,
                version.Evaluation.Id,
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

    public async Task<ResumeWorkspaceDto?> AnalyzeResumeVersionAsync(
        Guid resumeVersionId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = await AcquireResumeMutationLockAsync(resolvedUserEmail, cancellationToken);
        try
        {
            var user = await GetOrCreateUserAsync(resolvedUserEmail, cancellationToken);
            var version = await LoadOwnedVersionAsync(resumeVersionId, user.Id, cancellationToken);
            if (version is null)
            {
                return null;
            }

            await ReevaluateVersionAsync(version, user, _clock.UtcNow, "analyze", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await GetWorkspaceAsync(user.Email, cancellationToken);
        }
        finally
        {
            ReleaseResumeMutationLock(mutationLock);
        }
    }

    public async Task<ResumeWorkspaceDto?> PatchStructuredDataAsync(
        Guid resumeVersionId,
        PatchResumeStructuredDataRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = await AcquireResumeMutationLockAsync(resolvedUserEmail, cancellationToken);
        try
        {
            var user = await GetOrCreateUserAsync(resolvedUserEmail, cancellationToken);
            var version = await LoadOwnedVersionAsync(resumeVersionId, user.Id, cancellationToken);
            if (version is null)
            {
                return null;
            }

            var normalized = NormalizePatchRequest(request);
            var structuredData = DeserializeStructuredData(version.StructuredDataJson);
            var nowUtc = _clock.UtcNow;
            foreach (var operation in normalized.Operations)
            {
                structuredData = ApplyStructuredDataPatch(structuredData, operation);
                _dbContext.ResumeCorrections.Add(
                    new ResumeCorrection
                    {
                        ResumeVersionId = version.Id,
                        FieldPath = operation.Path.TrimStart('/').Replace("/", ".", StringComparison.Ordinal),
                        PreviousValue = null,
                        CorrectedValue = operation.Op.Equals("remove", StringComparison.OrdinalIgnoreCase)
                            ? "__removed__"
                            : JsonSerializer.Serialize(operation.Value, JsonOptions),
                        Note = normalized.Note,
                        CreatedAtUtc = nowUtc
                    });
            }

            ApplyStructuredSnapshotToVersion(version, structuredData, nowUtc, "section_patch");
            await ReevaluateVersionAsync(version, user, nowUtc, "section_patch", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await GetWorkspaceAsync(user.Email, cancellationToken);
        }
        finally
        {
            ReleaseResumeMutationLock(mutationLock);
        }
    }


    public async Task<ResumeWorkspaceDto?> AddCorrectionAsync(
        Guid resumeVersionId,
        CorrectResumeFieldRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var mutationLock = await AcquireResumeMutationLockAsync(resolvedUserEmail, cancellationToken);
        try
        {
            var user = await GetOrCreateUserAsync(resolvedUserEmail, cancellationToken);
            var normalized = NormalizeCorrection(request);
            var version = await _dbContext.ResumeVersions
                .Include(candidate => candidate.ResumeDocument)
                .Include(candidate => candidate.Corrections)
                .Include(candidate => candidate.Evaluation)
                .Where(candidate =>
                    candidate.Id == resumeVersionId &&
                    candidate.ResumeDocument.UserId == user.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (version is null)
            {
                return null;
            }

            var nowUtc = _clock.UtcNow;
            version.ResumeDocument.UpdatedAtUtc = nowUtc;
            var correction = new ResumeCorrection
            {
                ResumeVersionId = resumeVersionId,
                FieldPath = normalized.FieldPath,
                PreviousValue = normalized.PreviousValue,
                CorrectedValue = normalized.CorrectedValue,
                Note = normalized.Note,
                CreatedAtUtc = nowUtc
            };
            _dbContext.ResumeCorrections.Add(correction);
            var corrections = version.Corrections.Concat([correction]).ToList();
            var extraction = ApplyCorrections(
                ExtractResume(version.ResumeDocument.SourceText),
                corrections);
            var structuredData = BuildStructuredData(
                version.ResumeDocument.SourceText,
                extraction.Skills,
                extraction.ProjectHighlights);
            structuredData = ApplyStructuredCorrections(structuredData, corrections);
            version.Summary = extraction.Summary;
            version.SkillsJson = SerializeList(extraction.Skills);
            version.ProjectHighlightsJson = SerializeList(extraction.ProjectHighlights);
            version.StructuredDataJson = SerializeStructuredData(structuredData);
            version.ExtractionMetadataJson = JsonSerializer.Serialize(
                BuildExtractionMetadata(
                    version.ResumeDocument.SourceKind,
                    version.ResumeDocument.SourceFileName,
                    version.ResumeDocument.SourceText,
                    structuredData,
                    "correction",
                    nowUtc,
                    ResumePromptVersions.DeterministicExtraction),
                JsonOptions);
            version.ExperienceCount = Math.Max(extraction.ExperienceCount, structuredData.Experience.Count);
            version.ProjectCount = Math.Max(extraction.ProjectCount, structuredData.Projects.Count);
            var context = await LoadEvaluationContextAsync(user.Id, cancellationToken);
            var runId = Guid.NewGuid();
            var modelEnhanced = await EvaluateResumeWithModelAsync(
                version.ResumeDocument.SourceText,
                extraction,
                context,
                corrections,
                user.Email,
                runId,
                cancellationToken);
            extraction = modelEnhanced.Extraction;
            var evaluationResult = modelEnhanced.Evaluation;
            if (modelEnhanced.StructuredData is not null)
            {
                structuredData = ApplyStructuredCorrections(modelEnhanced.StructuredData, corrections);
                version.StructuredDataJson = SerializeStructuredData(structuredData);
                version.SkillsJson = SerializeList(FlattenSkills(structuredData.Skills));
                version.ProjectHighlightsJson = SerializeList(
                    structuredData.Projects
                        .Select(project => string.IsNullOrWhiteSpace(project.Description)
                            ? project.Name
                            : $"{project.Name}: {project.Description}")
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Take(12)
                        .ToList());
                version.ExperienceCount = Math.Max(extraction.ExperienceCount, structuredData.Experience.Count);
                version.ProjectCount = Math.Max(extraction.ProjectCount, structuredData.Projects.Count);
                version.ExtractionMetadataJson = JsonSerializer.Serialize(
                    BuildExtractionMetadata(
                        version.ResumeDocument.SourceKind,
                        version.ResumeDocument.SourceFileName,
                        version.ResumeDocument.SourceText,
                        structuredData,
                        "correction",
                        nowUtc,
                        modelEnhanced.PromptVersion),
                    JsonOptions);
            }
            version.EvaluationRunId = runId.ToString("N");
            if (version.Evaluation is null)
            {
                version.Evaluation = new ResumeEvaluation
                {
                    ResumeVersionId = version.Id,
                    ResumeVersion = version
                };
                _dbContext.ResumeEvaluations.Add(version.Evaluation);
            }

            version.Evaluation.Score = evaluationResult.Score;
            version.Evaluation.Headline = evaluationResult.Headline;
            version.Evaluation.Verdict = evaluationResult.Verdict;
            version.Evaluation.StrengthsJson = SerializeList(evaluationResult.Strengths);
            version.Evaluation.GapsJson = SerializeList(evaluationResult.Gaps);
            version.Evaluation.NextActionsJson = SerializeList(evaluationResult.NextActions);
            version.Evaluation.SectionReviewsJson = JsonSerializer.Serialize(evaluationResult.SectionReviews, JsonOptions);
            version.Evaluation.EvidenceJson = JsonSerializer.Serialize(evaluationResult.Evidence, JsonOptions);
            version.Evaluation.CreatedAtUtc = nowUtc;
            await UpsertResumeEvaluationMemoryAsync(
                user.Id,
                version.ResumeDocument.Title,
                version.Evaluation.Id,
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

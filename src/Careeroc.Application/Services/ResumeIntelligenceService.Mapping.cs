using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Profile;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
    private async Task<Dictionary<string, ResumeModelRunDto>> LoadLatestResumeModelRunsAsync(
        IReadOnlyList<ResumeDocument> documents,
        CancellationToken cancellationToken)
    {
        var runIds = documents
            .Select(document => document.Versions.OrderByDescending(version => version.CreatedAtUtc).FirstOrDefault()?.EvaluationRunId)
            .Where(runId => !string.IsNullOrWhiteSpace(runId))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (runIds.Count == 0)
        {
            return [];
        }

        var modelCalls = await _dbContext.ModelCallEvents
            .AsNoTracking()
            .Where(candidate => candidate.RunId != null &&
                                runIds.Contains(candidate.RunId) &&
                                candidate.Purpose == "resume_evaluation")
            .OrderByDescending(candidate => candidate.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        return modelCalls
            .GroupBy(modelCall => modelCall.RunId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var modelCall = group.First();
                    return new ResumeModelRunDto(
                        group.Key,
                        modelCall.Purpose,
                        modelCall.Provider,
                        modelCall.Model,
                        modelCall.Outcome,
                        modelCall.PromptTokens,
                        modelCall.CompletionTokens,
                        modelCall.TotalTokens,
                        modelCall.EstimatedCostUsd,
                        modelCall.LatencyMs,
                        modelCall.OccurredAtUtc,
                        modelCall.ErrorType,
                        modelCall.StatusCode);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static ResumeDocumentDto MapDocument(
        ResumeDocument document,
        IReadOnlyDictionary<string, ResumeModelRunDto> modelRuns,
        IReadOnlyList<UserMemoryEntryDto>? memoryProposals = null)
    {
        var latestVersion = document.Versions
            .OrderByDescending(version => version.CreatedAtUtc)
            .FirstOrDefault();
        var modelRun = latestVersion?.EvaluationRunId is { Length: > 0 } runId &&
                       modelRuns.TryGetValue(runId, out var matchedModelRun)
            ? matchedModelRun
            : latestVersion?.EvaluationRunId is { Length: > 0 } missingRunId
                ? new ResumeModelRunDto(missingRunId, "resume_evaluation", null, null, "not_recorded", null, null, null, null, null, null, null, null)
                : null;

        return new ResumeDocumentDto(
            document.Id,
            document.Title,
            document.SourceKind,
            document.SourceFileName,
            document.SourceText.Length,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            latestVersion is null ? null : MapVersion(document, latestVersion),
            latestVersion?.Evaluation is null ? null : MapEvaluation(latestVersion.Evaluation, modelRun),
            memoryProposals);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<UserMemoryEntryDto>>> LoadResumeMemoryProposalsAsync(
        Guid userId,
        IReadOnlyList<ResumeDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var documentIds = documents.Select(document => document.Id).ToHashSet();
        var versionToDocument = documents
            .SelectMany(document => document.Versions.Select(version => new { version.Id, DocumentId = document.Id }))
            .ToDictionary(version => version.Id, version => version.DocumentId);
        var sourceIds = documentIds
            .Concat(versionToDocument.Keys)
            .ToHashSet();
        var nowUtc = _clock.UtcNow;
        var proposals = await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == userId &&
                entry.ConfirmationStatus == "pending" &&
                entry.SourceId.HasValue &&
                sourceIds.Contains(entry.SourceId.Value) &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return proposals
            .GroupBy(
                proposal => documentIds.Contains(proposal.SourceId!.Value)
                    ? proposal.SourceId!.Value
                    : versionToDocument.GetValueOrDefault(proposal.SourceId!.Value))
            .Where(group => group.Key != Guid.Empty)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UserMemoryEntryDto>)group
                    .Take(5)
                    .Select(MapMemoryProposal)
                    .ToList());
    }

    private static UserMemoryEntryDto MapMemoryProposal(UserMemoryEntry entry)
    {
        return new UserMemoryEntryDto(
            entry.Id,
            entry.Kind,
            entry.Key,
            entry.Value,
            entry.Summary,
            entry.SourceType,
            entry.SourceId,
            entry.Confidence,
            entry.ConfirmationStatus,
            entry.UpdatedAtUtc,
            entry.ExpiresAtUtc);
    }

    private static ResumeVersionDto MapVersion(ResumeDocument document, ResumeVersion version)
    {
        var skills = DeserializeList(version.SkillsJson);
        var projectHighlights = DeserializeList(version.ProjectHighlightsJson);
        var structuredData = DeserializeStructuredData(version.StructuredDataJson);
        if (structuredData.Experience.Count == 0 &&
            structuredData.Education.Count == 0 &&
            structuredData.Projects.Count == 0 &&
            FlattenSkills(structuredData.Skills).Count == 0)
        {
            structuredData = BuildStructuredData(document.SourceText, skills, projectHighlights);
        }

        return new ResumeVersionDto(
            version.Id,
            version.ResumeDocumentId,
            version.Summary,
            skills,
            projectHighlights,
            structuredData,
            DeserializeExtractionMetadata(version.ExtractionMetadataJson),
            Math.Max(version.ExperienceCount, structuredData.Experience.Count),
            Math.Max(version.ProjectCount, structuredData.Projects.Count),
            version.CreatedAtUtc,
            version.Corrections
                .OrderByDescending(correction => correction.CreatedAtUtc)
                .Select(MapCorrection)
                .ToList());
    }

    private static ResumeCorrectionDto MapCorrection(ResumeCorrection correction)
    {
        return new ResumeCorrectionDto(
            correction.Id,
            correction.ResumeVersionId,
            correction.FieldPath,
            correction.PreviousValue,
            correction.CorrectedValue,
            correction.Note,
            correction.CreatedAtUtc);
    }

    private static ResumeEvaluationDto MapEvaluation(
        ResumeEvaluation evaluation,
        ResumeModelRunDto? modelRun)
    {
        var evidence = JsonSerializer.Deserialize<ResumeEvaluationEvidenceDto>(
            evaluation.EvidenceJson,
            JsonOptions) ?? new ResumeEvaluationEvidenceDto(0, 0, 0, false, false, false, 0, 0, null);

        return new ResumeEvaluationDto(
            evaluation.Id,
            evaluation.ResumeVersionId,
            evaluation.Score,
            string.IsNullOrWhiteSpace(evaluation.Headline)
                ? "Resume review is ready."
                : evaluation.Headline,
            evaluation.Verdict,
            DeserializeList(evaluation.StrengthsJson),
            DeserializeList(evaluation.GapsJson),
            DeserializeList(evaluation.NextActionsJson),
            DeserializeSectionReviews(evaluation.SectionReviewsJson),
            evidence,
            modelRun,
            evaluation.CreatedAtUtc);
    }

    private static ResumeReadinessDto BuildReadiness(IReadOnlyList<ResumeDocument> documents)
    {
        if (documents.Count == 0)
        {
            return new ResumeReadinessDto(
                false,
                "No resume has been added yet.",
                ["Paste a resume to start extraction and evaluation."]);
        }

        var latest = documents
            .OrderByDescending(document => document.UpdatedAtUtc)
            .First();
        return new ResumeReadinessDto(
            true,
            $"Latest resume is {latest.Title}.",
            ["Review the extracted fields and compare the score against target applications."]);
    }

    private static string SerializeList(IReadOnlyList<string> values)
    {
        return JsonSerializer.Serialize(values, JsonOptions);
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
}

namespace Careeroc.Contracts.Resumes;

using Careeroc.Contracts.Profile;
using System.Text.Json;

public sealed record ResumeWorkspaceDto(
    IReadOnlyList<ResumeDocumentDto> Documents,
    ResumeReadinessDto Readiness);

public sealed record ResumeDocumentDto(
    Guid Id,
    string Title,
    string SourceKind,
    string? SourceFileName,
    int RawSourceCharacterCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ResumeVersionDto? LatestVersion,
    ResumeEvaluationDto? LatestEvaluation,
    IReadOnlyList<UserMemoryEntryDto>? MemoryProposals = null);

public sealed record ResumeVersionDto(
    Guid Id,
    Guid ResumeDocumentId,
    string Summary,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> ProjectHighlights,
    ResumeStructuredDataDto StructuredData,
    ResumeExtractionMetadataDto ExtractionMetadata,
    int ExperienceCount,
    int ProjectCount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ResumeCorrectionDto> Corrections);

public sealed record ResumeStructuredDataDto(
    ResumeContactDto Contact,
    IReadOnlyList<ResumeExperienceDto> Experience,
    IReadOnlyList<ResumeEducationDto> Education,
    ResumeSkillGroupsDto Skills,
    IReadOnlyList<ResumeProjectDto> Projects);

public sealed record ResumeContactDto(
    string Name,
    string Email,
    string Phone,
    string Location,
    IReadOnlyList<ResumeLinkDto> Links);

public sealed record ResumeLinkDto(
    string Label,
    string Url);

public sealed record ResumeExperienceDto(
    string Company,
    string Title,
    string Location,
    string DateRange,
    IReadOnlyList<string> Bullets);

public sealed record ResumeEducationDto(
    string School,
    string Degree,
    string Field,
    string DateRange,
    string Gpa,
    IReadOnlyList<string> Notes);

public sealed record ResumeSkillGroupsDto(
    IReadOnlyList<string> Technical,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Other);

public sealed record ResumeProjectDto(
    string Name,
    string Description,
    IReadOnlyList<string> Tech,
    IReadOnlyList<string> Links,
    IReadOnlyList<string> Bullets);

public sealed record ResumeCorrectionDto(
    Guid Id,
    Guid ResumeVersionId,
    string FieldPath,
    string? PreviousValue,
    string CorrectedValue,
    string? Note,
    DateTimeOffset CreatedAtUtc);

public sealed record ResumeEvaluationDto(
    Guid Id,
    Guid ResumeVersionId,
    int Score,
    string Headline,
    string Verdict,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> NextActions,
    IReadOnlyList<ResumeSectionReviewDto> SectionReviews,
    ResumeEvaluationEvidenceDto Evidence,
    ResumeModelRunDto? ModelRun,
    DateTimeOffset CreatedAtUtc);

public sealed record ResumeExtractionMetadataDto(
    string SourceKind,
    string? SourceFileName,
    int RawSourceCharacterCount,
    string ExtractionMethod,
    DateTimeOffset ExtractedAtUtc,
    int ContactFieldCount,
    int ExperienceCount,
    int EducationCount,
    int ProjectCount,
    int SkillCount,
    string? PromptVersion,
    string OcrStatus = "not_required",
    decimal? OcrConfidence = null,
    string? OcrError = null);

public sealed record ResumeSectionReviewDto(
    string Section,
    int Score,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Suggestions);

public sealed record ResumeModelRunDto(
    string RunId,
    string Purpose,
    string? Provider,
    string? Model,
    string Outcome,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal? EstimatedCostUsd,
    int? LatencyMs,
    DateTimeOffset? OccurredAtUtc,
    string? ErrorType,
    int? StatusCode);

public sealed record ResumeEvaluationEvidenceDto(
    int SkillCount,
    int ExperienceCount,
    int ProjectCount,
    bool HasEducation,
    bool HasContact,
    bool HasLinks,
    int ApplicationCount,
    int InterviewOrOfferCount,
    string? TargetRole);

public sealed record ResumeReadinessDto(
    bool HasResume,
    string Summary,
    IReadOnlyList<string> NextActions);

public sealed record PatchResumeStructuredDataRequest(
    IReadOnlyList<ResumeStructuredDataPatchOperation> Operations,
    string? Note);

public sealed record ResumeStructuredDataPatchOperation(
    string Op,
    string Path,
    JsonElement Value);

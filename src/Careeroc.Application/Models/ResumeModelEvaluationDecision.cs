using Careeroc.Contracts.Resumes;

namespace Careeroc.Application.Models;

public sealed record ResumeModelEvaluationDecision(
    int? Score,
    string? Verdict,
    IReadOnlyList<string>? Strengths,
    IReadOnlyList<string>? Gaps,
    IReadOnlyList<string>? NextActions,
    string? Summary,
    IReadOnlyList<string>? Skills,
    IReadOnlyList<string>? ProjectHighlights,
    int? ExperienceCount,
    int? ProjectCount,
    bool? HasEducation,
    bool? HasContact,
    bool? HasLinks,
    IReadOnlyList<ResumeSectionReviewDto>? SectionReviews,
    decimal Confidence,
    string AgentName,
    ResumeStructuredDataDto? StructuredData = null);

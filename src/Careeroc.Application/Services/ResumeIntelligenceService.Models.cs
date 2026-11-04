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
    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[\w.+-]+@[\w.-]+\.[a-z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\+?\d[\d\s().-]{7,}\d")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b(20\d{2}|19\d{2})\b")]
    private static partial Regex DateRangeRegex();

    [GeneratedRegex(@"\b(CONTACT|SUMMARY|EDUCATION|WORK EXPERIENCE|PROFESSIONAL EXPERIENCE|EXPERIENCE|TECHNICAL PROJECTS|PROJECTS|TECHNICAL SKILLS|SKILLS|CERTIFICATIONS|PUBLICATIONS|AWARDS)\b")]
    private static partial Regex KnownResumeHeadingRegex();

    [GeneratedRegex(@"\b(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s*)?20\d{2}\s*-\s*(?:Present|Current|(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s*)?20\d{2})\b|\b20\d{2}\s*-\s*(?:Present|Current|20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRangeTextRegex();

    [GeneratedRegex(@"\b(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s+20\d{2}|(?:Spring|Summer|Fall|Winter)\s+20\d{2}|20\d{2})(?:\s*-\s*(?:Present|Current|(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s*)?20\d{2}|20\d{2}))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResumeDateTextRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z][^•\n]{2,160}\b(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s+20\d{2}|(?:Spring|Summer|Fall|Winter)\s+20\d{2})(?:\s*-\s*(?:Present|Current|(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s*)?20\d{2}|20\d{2}))?)")]
    private static partial Regex EmbeddedResumeEntryStartRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z][A-Za-z0-9&.,'()|/ -]{2,140}\b(?:(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?\s+20\d{2}|(?:Spring|Summer|Fall|Winter)\s+20\d{2}|20\d{2})\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DenseResumeEntryStartRegex();

    [GeneratedRegex(@"\s+(?:Jan|January|Feb|February|Mar|March|Apr|April|May|Jun|June|Jul|July|Aug|August|Sep|Sept|September|Oct|October|Nov|November|Dec|December)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingMonthRegex();

    [GeneratedRegex(@"\b[A-Z][A-Za-z .'-]+,\s*[A-Z]{2}\b")]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"\b(?:(?:[A-Z][A-Za-z&.'-]+,?|of|the|at|and)\s+){0,8}(?:University|College|Institute|School)(?:\s+(?:[A-Z][A-Za-z&.'-]+,?|of|the|at|and)){0,8}\b")]
    private static partial Regex SchoolRegex();

    [GeneratedRegex(@"\b(?:B\.?S\.?|M\.?S\.?|Bachelor(?:'s)?|Master(?:'s)?|Ph\.?D\.?|Doctorate)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DegreeRegex();

    [GeneratedRegex(@"\b(?:Computer Science(?: and Engineering)?|Software Engineering|Data Science|Statistics|Mathematics|Electrical Engineering|Machine Learning|Information Systems)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FieldOfStudyRegex();

    [GeneratedRegex(@"\bGPA[:\s]*[0-4](?:\.\d{1,2})?(?:\s*/\s*4(?:\.0)?)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex GpaRegex();

    [GeneratedRegex(@"\b(?:Software Engineer(?:ing)?(?: Intern)?|Data Engineer(?: Intern)?|Cloud Engineer(?: Intern)?|Research Assistant|Teaching Assistant|Developer|Backend Engineer|Frontend Engineer|Full Stack Engineer|Machine Learning Engineer|Product Engineer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExperienceTitleRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.\[\]-]{0,239}$")]
    private static partial Regex FieldPathRegex();

    private sealed record ResumeExtraction(
        string Summary,
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> ProjectHighlights,
        int ExperienceCount,
        int ProjectCount,
        bool HasEducation,
        bool HasContact,
        bool HasLinks,
        ResumeStructuredDataDto StructuredData);

    private sealed record ResumeEvaluationContext(
        string? TargetRole,
        string? Location,
        int ApplicationCount,
        int InterviewOrOfferCount,
        ResumeMailboxContext MailboxContext);

    private sealed record ResumeEvaluationResult(
        int Score,
        string Headline,
        string Verdict,
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Gaps,
        IReadOnlyList<string> NextActions,
        IReadOnlyList<ResumeSectionReviewDto> SectionReviews,
        ResumeEvaluationEvidenceDto Evidence);

    private sealed record ModelEnhancedResumeEvaluation(
        ResumeExtraction Extraction,
        ResumeEvaluationResult Evaluation,
        ResumeStructuredDataDto? StructuredData,
        string? PromptVersion);

    private sealed record NormalizedCorrection(
        string FieldPath,
        string? PreviousValue,
        string CorrectedValue,
        string? Note);

    private sealed record NormalizedPatchRequest(
        IReadOnlyList<NormalizedPatchOperation> Operations,
        string? Note);

    private sealed record NormalizedPatchOperation(
        string Op,
        string Path,
        JsonElement Value);

    private sealed record PdfPageImage(
        byte[] Bytes,
        string ContentType,
        int PixelCount);

    private sealed record ResumeSourceImportResult(
        string SourceText,
        ResumeOcrImportMetadata OcrMetadata);

    private sealed record ResumeOcrImportMetadata(
        string Status,
        decimal? Confidence = null,
        string? Error = null);

    private sealed record ResumeEvaluationMemory(
        string ResumeTitle,
        int Score,
        IReadOnlyList<string> Skills,
        int ExperienceCount,
        int ProjectCount,
        bool HasEducation,
        bool HasContact,
        bool HasLinks,
        IReadOnlyList<string> NextActions);
}

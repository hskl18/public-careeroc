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
    private async Task<ModelEnhancedResumeEvaluation> EvaluateResumeWithModelAsync(
        string sourceText,
        ResumeExtraction extraction,
        ResumeEvaluationContext context,
        IEnumerable<ResumeCorrection> corrections,
        string userEmail,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var baseline = EvaluateResume(extraction, context);
        var modelDecision = await _resumeEvaluationModel.EvaluateAsync(
            new ResumeModelEvaluationRequest(
                sourceText,
                extraction.Summary,
                extraction.Skills,
                extraction.ProjectHighlights,
                extraction.ExperienceCount,
                extraction.ProjectCount,
                extraction.HasEducation,
                extraction.HasContact,
                extraction.HasLinks,
                context.TargetRole,
                context.ApplicationCount,
                context.InterviewOrOfferCount,
                corrections
                    .OrderBy(correction => correction.CreatedAtUtc)
                    .Select(correction => new ResumeModelCorrection(
                        correction.FieldPath,
                        correction.CorrectedValue,
                        correction.Note))
                    .ToList(),
                context.MailboxContext),
            cancellationToken,
            new ModelCallContext(userEmail, null, null, runId.ToString("N")));

        if (modelDecision is null || modelDecision.Confidence < 0.55m)
        {
            return new ModelEnhancedResumeEvaluation(extraction, baseline, null, null);
        }

        var enhancedExtraction = ApplyModelDecisionToExtraction(extraction, modelDecision);
        var enhancedStructuredData = NormalizeModelStructuredData(modelDecision.StructuredData);
        var enhancedEvaluation = ApplyModelDecisionToEvaluation(
            EvaluateResume(enhancedExtraction, context),
            modelDecision,
            enhancedExtraction,
            context);
        return new ModelEnhancedResumeEvaluation(
            enhancedExtraction,
            enhancedEvaluation,
            enhancedStructuredData,
            ResumePromptVersions.ExtractAnalyze);
    }

    private static ResumeEvaluationResult EvaluateResume(
        ResumeExtraction extraction,
        ResumeEvaluationContext context)
    {
        var score = 25;
        var strengths = new List<string>();
        var gaps = new List<string>();
        var nextActions = new List<string>();

        if (extraction.HasContact)
        {
            score += 10;
            strengths.Add("Contact information is present.");
        }
        else
        {
            gaps.Add("Contact information was not detected.");
            nextActions.Add("Add email, phone, location, and portfolio links near the top.");
        }

        if (extraction.HasEducation)
        {
            score += 10;
            strengths.Add("Education signal is present.");
        }
        else
        {
            gaps.Add("Education section was not detected.");
            nextActions.Add("Add education details or make the section heading clearer.");
        }

        if (extraction.Skills.Count >= 6)
        {
            score += 20;
            strengths.Add($"{extraction.Skills.Count} technical skill signals were detected.");
        }
        else
        {
            score += extraction.Skills.Count * 2;
            gaps.Add("Technical skill coverage looks thin.");
            nextActions.Add("Add a concise skills section aligned to the target role.");
        }

        if (extraction.ExperienceCount > 0)
        {
            score += Math.Min(15, extraction.ExperienceCount * 5);
            strengths.Add("Experience or role history is visible.");
        }
        else
        {
            gaps.Add("No clear experience entries were detected.");
            nextActions.Add("Add role entries with title, organization, dates, and quantified impact.");
        }

        if (extraction.ProjectCount > 0)
        {
            score += Math.Min(10, extraction.ProjectCount * 5);
            strengths.Add("Project evidence is visible.");
        }
        else
        {
            gaps.Add("No project evidence was detected.");
            nextActions.Add("Add two or three projects with technologies, scope, and outcomes.");
        }

        if (extraction.HasLinks)
        {
            score += 5;
            strengths.Add("Source links are present.");
        }
        else
        {
            gaps.Add("No portfolio, GitHub, or LinkedIn links were detected.");
        }

        if (!string.IsNullOrWhiteSpace(context.TargetRole))
        {
            score += 5;
            strengths.Add($"Evaluation is anchored to target role: {context.TargetRole}.");
        }
        else
        {
            gaps.Add("No target role is set in profile.");
            nextActions.Add("Set a target role so resume review can judge fit.");
        }

        if (context.InterviewOrOfferCount > 0)
        {
            score += 5;
            strengths.Add("Mailbox history includes interview or offer signal to compare against.");
        }

        var qualityGaps = BuildStructuredQualityGaps(extraction).Take(5).ToList();
        foreach (var qualityGap in qualityGaps)
        {
            gaps.Add(qualityGap);
        }

        score -= Math.Min(12, qualityGaps.Count * 3);

        score = Math.Clamp(score, 0, 100);
        if (nextActions.Count == 0)
        {
            nextActions.Add("Use the resume agent to turn strengths into a targeted version.");
        }

        return new ResumeEvaluationResult(
            score,
            BuildHeadline(score, extraction),
            BuildVerdict(score, context.TargetRole),
            strengths,
            gaps,
            nextActions.Take(5).ToList(),
            BuildSectionReviews(extraction, score),
            new ResumeEvaluationEvidenceDto(
                extraction.Skills.Count,
                extraction.ExperienceCount,
                extraction.ProjectCount,
                extraction.HasEducation,
                extraction.HasContact,
                extraction.HasLinks,
                context.ApplicationCount,
                context.InterviewOrOfferCount,
                context.TargetRole));
    }

    private static ResumeExtraction ApplyModelDecisionToExtraction(
        ResumeExtraction extraction,
        ResumeModelEvaluationDecision decision)
    {
        return extraction with
        {
            Summary = NormalizeModelText(decision.Summary, extraction.Summary, 1000),
            Skills = NormalizeModelList(decision.Skills, extraction.Skills, 40),
            ProjectHighlights = NormalizeModelList(decision.ProjectHighlights, extraction.ProjectHighlights, 12),
            ExperienceCount = NormalizeModelCount(decision.ExperienceCount, extraction.ExperienceCount),
            ProjectCount = NormalizeModelCount(decision.ProjectCount, extraction.ProjectCount),
            HasEducation = decision.HasEducation ?? extraction.HasEducation,
            HasContact = decision.HasContact ?? extraction.HasContact,
            HasLinks = decision.HasLinks ?? extraction.HasLinks,
            StructuredData = NormalizeModelStructuredData(decision.StructuredData) ?? extraction.StructuredData
        };
    }

    private static ResumeStructuredDataDto? NormalizeModelStructuredData(
        ResumeStructuredDataDto? structuredData)
    {
        if (structuredData is null)
        {
            return null;
        }

        var normalized = NormalizeStructuredData(structuredData);
        var hasUsefulContent =
            !string.IsNullOrWhiteSpace(normalized.Contact.Email) ||
            !string.IsNullOrWhiteSpace(normalized.Contact.Phone) ||
            normalized.Contact.Links.Count > 0 ||
            normalized.Experience.Count > 0 ||
            normalized.Education.Count > 0 ||
            FlattenSkills(normalized.Skills).Count > 0 ||
            normalized.Projects.Count > 0;

        return hasUsefulContent ? normalized : null;
    }

    private static ResumeEvaluationResult ApplyModelDecisionToEvaluation(
        ResumeEvaluationResult baseline,
        ResumeModelEvaluationDecision decision,
        ResumeExtraction extraction,
        ResumeEvaluationContext context)
    {
        var score = decision.Score.HasValue
            ? Math.Clamp(decision.Score.Value, 0, 100)
            : baseline.Score;
        var strengths = NormalizeModelList(decision.Strengths, baseline.Strengths, 8);
        var gaps = MergeModelListWithBaseline(
            NormalizeModelList(decision.Gaps, baseline.Gaps, 8),
            baseline.Gaps,
            8);
        var nextActions = NormalizeModelList(decision.NextActions, baseline.NextActions, 5);
        var sectionReviews = MergeModelSectionReviewsWithBaseline(
            NormalizeModelSectionReviews(decision.SectionReviews, baseline.SectionReviews),
            baseline.SectionReviews);
        var verdict = NormalizeModelText(
            decision.Verdict,
            BuildVerdict(score, context.TargetRole),
            1000);

        if (!strengths.Any(strength => strength.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                                      strength.Contains("gemma", StringComparison.OrdinalIgnoreCase)))
        {
            strengths = strengths
                .Append($"{decision.AgentName} reviewed the deterministic resume extraction.")
                .Take(8)
                .ToList();
        }

        return new ResumeEvaluationResult(
            score,
            BuildHeadline(score, extraction),
            verdict,
            strengths,
            gaps,
            nextActions,
            sectionReviews,
            new ResumeEvaluationEvidenceDto(
                extraction.Skills.Count,
                extraction.ExperienceCount,
                extraction.ProjectCount,
                extraction.HasEducation,
                extraction.HasContact,
                extraction.HasLinks,
                context.ApplicationCount,
                context.InterviewOrOfferCount,
                context.TargetRole));
    }

    private static string NormalizeModelText(string? value, string fallback, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static IReadOnlyList<string> NormalizeModelList(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> fallback,
        int maxItems)
    {
        if (values is null || values.Count == 0)
        {
            return fallback;
        }

        var normalized = values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();

        return normalized.Count > 0 ? normalized : fallback;
    }

    private static IReadOnlyList<ResumeSectionReviewDto> NormalizeModelSectionReviews(
        IReadOnlyList<ResumeSectionReviewDto>? values,
        IReadOnlyList<ResumeSectionReviewDto> fallback)
    {
        if (values is null || values.Count == 0)
        {
            return fallback;
        }

        var normalized = values
            .Select(
                review => new ResumeSectionReviewDto(
                    NormalizeSectionName(review.Section),
                    Math.Clamp(review.Score, 0, 100),
                    NormalizeModelList(review.Strengths, [], 5),
                    NormalizeModelList(review.Weaknesses, [], 5),
                    NormalizeModelList(review.Suggestions, [], 5)))
            .Where(
                review =>
                    !string.IsNullOrWhiteSpace(review.Section) &&
                    (review.Strengths.Count > 0 ||
                     review.Weaknesses.Count > 0 ||
                     review.Suggestions.Count > 0))
            .GroupBy(review => review.Section, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();

        return normalized.Count > 0 ? normalized : fallback;
    }

    private static IReadOnlyList<string> MergeModelListWithBaseline(
        IReadOnlyList<string> preferred,
        IReadOnlyList<string> baseline,
        int maxItems)
    {
        return preferred
            .Concat(baseline)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static IReadOnlyList<ResumeSectionReviewDto> MergeModelSectionReviewsWithBaseline(
        IReadOnlyList<ResumeSectionReviewDto> preferred,
        IReadOnlyList<ResumeSectionReviewDto> baseline)
    {
        var preferredBySection = preferred.ToDictionary(
            review => review.Section,
            StringComparer.OrdinalIgnoreCase);
        var orderedSections = preferred
            .Select(review => review.Section)
            .Concat(baseline.Select(review => review.Section))
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return orderedSections
            .Select(section =>
            {
                var hasPreferred = preferredBySection.TryGetValue(section, out var modelReview);
                var baselineReview = baseline.FirstOrDefault(
                    review => review.Section.Equals(section, StringComparison.OrdinalIgnoreCase));

                if (!hasPreferred)
                {
                    return baselineReview!;
                }

                if (baselineReview is null)
                {
                    return modelReview!;
                }

                return new ResumeSectionReviewDto(
                    modelReview!.Section,
                    Math.Min(modelReview.Score, baselineReview.Score),
                    MergeModelListWithBaseline(modelReview.Strengths, baselineReview.Strengths, 5),
                    MergeModelListWithBaseline(modelReview.Weaknesses, baselineReview.Weaknesses, 5),
                    MergeModelListWithBaseline(modelReview.Suggestions, baselineReview.Suggestions, 5));
            })
            .ToList();
    }

    private static string NormalizeSectionName(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace(' ', '_');
        return normalized switch
        {
            "contact" => "contact",
            "summary" => "summary",
            "experience" => "experience",
            "education" => "education",
            "skills" => "skills",
            "projects" => "projects",
            "overall" => "overall",
            _ => normalized
        };
    }

    private static int NormalizeModelCount(int? value, int fallback)
    {
        return value.HasValue ? Math.Clamp(value.Value, 0, 50) : fallback;
    }

    private static string BuildSummary(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> skills,
        int experienceCount,
        int projectCount)
    {
        var headline = lines.FirstOrDefault(line => line.Length is > 8 and < 140) ?? "Resume";
        return $"{headline} | {skills.Count} skills, {experienceCount} experience signals, {projectCount} project signals detected.";
    }

    private static string BuildVerdict(int score, string? targetRole)
    {
        var target = string.IsNullOrWhiteSpace(targetRole)
            ? "the current target role"
            : targetRole.Trim();
        return score >= 80
            ? $"Strong resume foundation for {target}; ready for targeted refinement."
            : score >= 60
                ? $"Usable resume foundation for {target}; improve evidence density before heavy outreach."
                : $"Resume needs more structured evidence before trusting agentic targeting for {target}.";
    }

    private static string BuildHeadline(int score, ResumeExtraction extraction)
    {
        var quality = score >= 80 ? "strong" : score >= 60 ? "solid" : "needs work";
        return $"{quality} resume with {extraction.ExperienceCount} experience signals and {extraction.ProjectCount} project signals.";
    }

    private static IReadOnlyList<ResumeSectionReviewDto> BuildSectionReviews(
        ResumeExtraction extraction,
        int overallScore)
    {
        var contactWeaknesses = BuildContactWeaknesses(extraction).Take(5).ToList();
        var experienceWeaknesses = BuildExperienceWeaknesses(extraction).Take(5).ToList();
        var educationWeaknesses = BuildEducationWeaknesses(extraction).Take(5).ToList();
        var skillWeaknesses = BuildSkillWeaknesses(extraction).Take(5).ToList();
        var projectWeaknesses = BuildProjectWeaknesses(extraction).Take(5).ToList();
        var overallWeaknesses = BuildStructuredQualityGaps(extraction)
            .Concat(overallScore >= 85
                ? ["Score is high, but section quality still needs manual cleanup before export."]
                : [])
            .Take(5)
            .ToList();

        return
        [
            new ResumeSectionReviewDto(
                "contact",
                ScoreSection(extraction.HasContact ? 90 : 35, contactWeaknesses),
                extraction.HasContact ? ["Contact information is detectable."] : [],
                contactWeaknesses,
                extraction.HasContact ? ["Verify portfolio and LinkedIn links are current."] : ["Add email, phone, location, GitHub, LinkedIn, and portfolio."]),
            new ResumeSectionReviewDto(
                "experience",
                ScoreSection(extraction.ExperienceCount > 0 ? Math.Clamp(55 + extraction.ExperienceCount * 8, 60, 92) : 30, experienceWeaknesses),
                extraction.ExperienceCount > 0 ? ["Role history is visible to the parser."] : [],
                experienceWeaknesses,
                ["Use title, company, location, dates, and quantified bullets for each role."]),
            new ResumeSectionReviewDto(
                "education",
                ScoreSection(extraction.HasEducation ? 85 : 40, educationWeaknesses),
                extraction.HasEducation ? ["Education signal is present."] : [],
                educationWeaknesses,
                ["Keep degree, school, date range, GPA or honors where useful."]),
            new ResumeSectionReviewDto(
                "skills",
                ScoreSection(extraction.Skills.Count >= 8 ? 88 : Math.Clamp(35 + extraction.Skills.Count * 6, 35, 78), skillWeaknesses),
                extraction.Skills.Count > 0 ? [$"{extraction.Skills.Count} technical skill signals detected."] : [],
                skillWeaknesses,
                ["Group skills by languages, frameworks, cloud, data, and ML tooling."]),
            new ResumeSectionReviewDto(
                "projects",
                ScoreSection(extraction.ProjectCount > 0 ? Math.Clamp(55 + extraction.ProjectCount * 10, 60, 90) : 35, projectWeaknesses),
                extraction.ProjectCount > 0 ? ["Project evidence is visible."] : [],
                projectWeaknesses,
                ["Keep each project outcome-focused and attach stack, dates, and metrics."]),
            new ResumeSectionReviewDto(
                "overall",
                overallScore,
                overallScore >= 70 ? ["Resume has enough structure for targeted review."] : [],
                overallWeaknesses.Count > 0
                    ? overallWeaknesses
                    : ["Resume needs stronger structured evidence before automation should trust it."],
                ["Use corrections to teach the next extraction before generating targeted versions."])
        ];
    }

    private static int ScoreSection(int baseScore, IReadOnlyCollection<string> weaknesses)
    {
        return Math.Clamp(baseScore - Math.Min(18, weaknesses.Count * 4), 0, 100);
    }

    private static IReadOnlyList<string> BuildStructuredQualityGaps(ResumeExtraction extraction)
    {
        return BuildContactWeaknesses(extraction)
            .Concat(BuildExperienceWeaknesses(extraction))
            .Concat(BuildEducationWeaknesses(extraction))
            .Concat(BuildSkillWeaknesses(extraction))
            .Concat(BuildProjectWeaknesses(extraction))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<string> BuildContactWeaknesses(ResumeExtraction extraction)
    {
        var contact = extraction.StructuredData.Contact;
        var weaknesses = new List<string>();

        if (string.IsNullOrWhiteSpace(contact.Name))
        {
            weaknesses.Add("Candidate name was not parsed into the contact section.");
        }

        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            weaknesses.Add("Email address is missing from structured contact data.");
        }

        if (string.IsNullOrWhiteSpace(contact.Phone))
        {
            weaknesses.Add("Phone number is missing from structured contact data.");
        }

        if (string.IsNullOrWhiteSpace(contact.Location))
        {
            weaknesses.Add("Location is missing from structured contact data.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(contact.Name) &&
                contact.Location.Contains(contact.Name, StringComparison.OrdinalIgnoreCase))
            {
                weaknesses.Add("Contact location appears to include the candidate name.");
            }

            if (LooksLikeLocationContainsTitle(contact.Location))
            {
                weaknesses.Add("Contact location appears to include a job title.");
            }
        }

        if (!extraction.HasLinks)
        {
            weaknesses.Add(SummaryMentionsProfileLinks(extraction.Summary)
                ? "Profile labels appear in the summary, but no portfolio/GitHub/LinkedIn URLs were captured."
                : "Portfolio, GitHub, or LinkedIn URLs were not captured.");
        }

        return weaknesses;
    }

    private static IReadOnlyList<string> BuildExperienceWeaknesses(ResumeExtraction extraction)
    {
        var entries = extraction.StructuredData.Experience;
        var weaknesses = new List<string>();

        if (entries.Count == 0)
        {
            weaknesses.Add("No clear role entries were detected.");
            return weaknesses;
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Company)))
        {
            weaknesses.Add("At least one role is missing a company.");
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Title)))
        {
            weaknesses.Add("At least one role is missing a title.");
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.DateRange)))
        {
            weaknesses.Add("At least one role is missing dates.");
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Location)))
        {
            weaknesses.Add("At least one role is missing a location.");
        }

        if (entries.Any(entry => !string.IsNullOrWhiteSpace(entry.Title) &&
                                 entry.Location.Contains(entry.Title, StringComparison.OrdinalIgnoreCase)))
        {
            weaknesses.Add("Some role locations include the job title and need cleanup.");
        }

        if (entries.Any(entry => entry.Bullets.Any(BulletLooksLikeMergedRoleHeader)))
        {
            weaknesses.Add("Some bullets appear to contain another role header or location.");
        }

        if (entries.Any(entry => entry.Bullets.Count < 2))
        {
            weaknesses.Add("At least one role has fewer than two achievement bullets.");
        }

        if (entries.SelectMany(entry => entry.Bullets).Any() &&
            entries.SelectMany(entry => entry.Bullets).All(bullet => !ContainsMetric(bullet)))
        {
            weaknesses.Add("Experience bullets need more measurable outcomes.");
        }

        return weaknesses;
    }

    private static IReadOnlyList<string> BuildEducationWeaknesses(ResumeExtraction extraction)
    {
        var entries = extraction.StructuredData.Education;
        var weaknesses = new List<string>();

        if (entries.Count == 0)
        {
            weaknesses.Add("Education section was not detected.");
            return weaknesses;
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.DateRange)))
        {
            weaknesses.Add("At least one education entry is missing dates.");
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Gpa)))
        {
            weaknesses.Add("GPA, honors, or coursework details are not captured for every education entry.");
        }

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Degree) || string.IsNullOrWhiteSpace(entry.Field)))
        {
            weaknesses.Add("At least one education entry is missing degree or field detail.");
        }

        return weaknesses;
    }

    private static IReadOnlyList<string> BuildSkillWeaknesses(ResumeExtraction extraction)
    {
        var skillGroups = extraction.StructuredData.Skills;
        var flattened = FlattenSkills(skillGroups);
        var weaknesses = new List<string>();

        if (flattened.Count < 6)
        {
            weaknesses.Add("Skill coverage looks thin for automated matching.");
        }

        if (skillGroups.Languages.Count == 0 && flattened.Any(IsLikelyProgrammingLanguage))
        {
            weaknesses.Add("Programming languages are mixed into technical skills instead of the language group.");
        }

        if (skillGroups.Other.Count == 0)
        {
            weaknesses.Add("No other skills or domain strengths are separated from technical tools.");
        }

        if (flattened.Any(skill => skill == skill.ToLowerInvariant() && skill.Length > 1))
        {
            weaknesses.Add("Some skill labels are all lowercase and may need canonical casing.");
        }

        return weaknesses;
    }

    private static IReadOnlyList<string> BuildProjectWeaknesses(ResumeExtraction extraction)
    {
        var projects = extraction.StructuredData.Projects;
        var weaknesses = new List<string>();

        if (projects.Count == 0)
        {
            weaknesses.Add("No clear project entries were detected.");
            return weaknesses;
        }

        if (projects.Any(project => string.IsNullOrWhiteSpace(project.Description)))
        {
            weaknesses.Add("At least one project is missing a short description.");
        }

        if (projects.Any(project => project.Tech.Count == 0))
        {
            weaknesses.Add("At least one project has no tech stack tags.");
        }

        if (projects.Any(project => project.Links.Count == 0))
        {
            weaknesses.Add("Project links are missing for at least one project.");
        }

        if (projects.Any(project => project.Name.Contains('§') || project.Name.Contains('|')))
        {
            weaknesses.Add("Some project names still contain parser artifacts or link separators.");
        }

        if (projects.Any(project => project.Bullets.Count < 2))
        {
            weaknesses.Add("At least one project has fewer than two evidence bullets.");
        }

        if (projects.SelectMany(project => project.Bullets).Any() &&
            projects.SelectMany(project => project.Bullets).All(bullet => !ContainsMetric(bullet)))
        {
            weaknesses.Add("Project bullets need more measurable outcomes.");
        }

        return weaknesses;
    }

    private static bool SummaryMentionsProfileLinks(string summary)
    {
        return summary.Contains("GitHub", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("LinkedIn", StringComparison.OrdinalIgnoreCase) ||
               summary.Contains("Portfolio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLocationContainsTitle(string value)
    {
        return ExperienceTitleRegex().IsMatch(value);
    }

    private static bool BulletLooksLikeMergedRoleHeader(string bullet)
    {
        return ExperienceTitleRegex().IsMatch(bullet) &&
               LocationRegex().IsMatch(bullet);
    }

    private static bool ContainsMetric(string value)
    {
        return value.Any(char.IsDigit) || value.Contains('%', StringComparison.Ordinal);
    }

    private static bool IsLikelyProgrammingLanguage(string skill)
    {
        return skill.Equals("Java", StringComparison.OrdinalIgnoreCase) ||
               skill.Equals("JavaScript", StringComparison.OrdinalIgnoreCase) ||
               skill.Equals("TypeScript", StringComparison.OrdinalIgnoreCase) ||
               skill.Equals("Python", StringComparison.OrdinalIgnoreCase) ||
               skill.Equals("C#", StringComparison.OrdinalIgnoreCase) ||
               skill.Equals("SQL", StringComparison.OrdinalIgnoreCase);
    }
}

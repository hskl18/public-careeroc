using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Enums;

namespace Careeroc.Application.Services.Agents;

public sealed class ReviewEvidenceAgentService
{
    private const decimal ManualReviewClassificationThreshold = 0.75m;
    private const decimal ManualReviewMatchingThreshold = 0.60m;
    private const decimal ModelEscalationThreshold = 0.60m;
    private const decimal ModelClearanceThreshold = 0.75m;
    private const int MaxReasonLength = 280;
    private const int MaxVisibleOutputLength = 240;

    private readonly IReviewEvidenceModel _reviewEvidenceModel;

    public ReviewEvidenceAgentService()
        : this(NullReviewEvidenceModel.Instance)
    {
    }

    public ReviewEvidenceAgentService(IReviewEvidenceModel reviewEvidenceModel)
    {
        _reviewEvidenceModel = reviewEvidenceModel;
    }

    public ReviewEvidenceDecision Evaluate(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match)
    {
        return EvaluateDeterministically(parsed, match);
    }

    public async Task<ReviewEvidenceDecision> EvaluateAsync(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(parsed, match, null, cancellationToken);
    }

    public async Task<ReviewEvidenceDecision> EvaluateAsync(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ModelCallContext? modelCallContext,
        CancellationToken cancellationToken = default)
    {
        var baselineDecision = EvaluateDeterministically(parsed, match);
        if (!ShouldRunModelSecondPass(parsed, match, baselineDecision))
        {
            return baselineDecision;
        }

        var modelDecision = await _reviewEvidenceModel.EvaluateAsync(
            parsed,
            match,
            baselineDecision,
            cancellationToken,
            modelCallContext);

        return MergeModelDecision(parsed, baselineDecision, modelDecision);
    }

    private static ReviewEvidenceDecision EvaluateDeterministically(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match)
    {
        if (ShouldAutoAcceptApplicationReceipt(parsed, match))
        {
            return new ReviewEvidenceDecision(
                false,
                null,
                "Cleared the workflow state after checking extraction and application-match confidence.",
                "Review Evidence Agent");
        }

        var reasons = new List<string>();
        var shouldEscalate = ShouldEscalateToManualReview(parsed, match);

        if (parsed.ClassificationConfidence < ManualReviewClassificationThreshold && shouldEscalate)
        {
            reasons.Add($"Low classification confidence ({parsed.ClassificationConfidence:0.00}).");
        }

        if (match.MatchingConfidence < ManualReviewMatchingThreshold && shouldEscalate)
        {
            reasons.Add($"Low application-match confidence ({match.MatchingConfidence:0.00}).");
        }

        if (parsed.Role == ApplicationDefaults.UnknownRole && shouldEscalate)
        {
            reasons.Add("Role could not be extracted confidently.");
        }

        if (parsed.Company == ApplicationDefaults.UnknownCompany && shouldEscalate)
        {
            reasons.Add("Company could not be extracted confidently.");
        }

        if (!string.IsNullOrWhiteSpace(match.ReviewReason) &&
            match.MatchingConfidence < ManualReviewMatchingThreshold &&
            shouldEscalate)
        {
            reasons.Add(match.ReviewReason);
        }

        return reasons.Count > 0
            ? new ReviewEvidenceDecision(
                true,
                string.Join(" ", reasons),
                "Escalated with thread evidence because the workflow state is not yet trustworthy.",
                "Review Evidence Agent")
            : new ReviewEvidenceDecision(
                false,
                null,
                "Cleared the workflow state after checking extraction and application-match confidence.",
                "Review Evidence Agent");
    }

    private static bool ShouldRunModelSecondPass(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision)
    {
        return baselineDecision.RequiresManualReview ||
               parsed.ActionRequired ||
               parsed.Category is EmailCategory.Offer
                   or EmailCategory.OaInvitation
                   or EmailCategory.InterviewInvitation
                   or EmailCategory.InterviewConfirmation
                   or EmailCategory.Rejection ||
               parsed.ClassificationConfidence < 0.90m ||
               match.MatchingConfidence < 0.85m;
    }

    private static ReviewEvidenceDecision MergeModelDecision(
        ParsedRecruitingEmail parsed,
        ReviewEvidenceDecision baselineDecision,
        ReviewEvidenceModelDecision? modelDecision)
    {
        if (modelDecision is null || modelDecision.Confidence < ModelEscalationThreshold)
        {
            return baselineDecision;
        }

        if (modelDecision.RequiresManualReview)
        {
            return new ReviewEvidenceDecision(
                true,
                NormalizeReason(modelDecision.Reason) ??
                NormalizeReason(baselineDecision.Reason) ??
                "Gemma review agent found unresolved workflow evidence.",
                NormalizeVisibleOutput(
                    modelDecision.VisibleOutput,
                    "Gemma review agent escalated because the workflow evidence still needs correction."),
                NormalizeAgentName(modelDecision.AgentName));
        }

        if (!baselineDecision.RequiresManualReview)
        {
            return new ReviewEvidenceDecision(
                false,
                null,
                NormalizeVisibleOutput(
                    modelDecision.VisibleOutput,
                    "Gemma review agent cleared the workflow state after a second-pass evidence check."),
                NormalizeAgentName(modelDecision.AgentName));
        }

        if (CanModelClearBaselineEscalation(parsed, modelDecision))
        {
            return new ReviewEvidenceDecision(
                false,
                null,
                NormalizeVisibleOutput(
                    modelDecision.VisibleOutput,
                    "Gemma review agent cleared the workflow state after a second-pass evidence check."),
                NormalizeAgentName(modelDecision.AgentName));
        }

        return baselineDecision;
    }

    private static bool CanModelClearBaselineEscalation(
        ParsedRecruitingEmail parsed,
        ReviewEvidenceModelDecision modelDecision)
    {
        if (modelDecision.Confidence < ModelClearanceThreshold)
        {
            return false;
        }

        return !string.Equals(parsed.Company, ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(parsed.Role, ApplicationDefaults.UnknownRole, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAgentName(string? agentName)
    {
        return string.IsNullOrWhiteSpace(agentName)
            ? "Review Evidence Agent"
            : agentName.Trim();
    }

    private static string NormalizeVisibleOutput(string? visibleOutput, string fallback)
    {
        return Truncate(string.IsNullOrWhiteSpace(visibleOutput) ? fallback : visibleOutput.Trim(), MaxVisibleOutputLength);
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason.Trim(), MaxReasonLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool ShouldEscalateToManualReview(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match)
    {
        if (parsed.Category == EmailCategory.GenericUpdate && !parsed.ActionRequired)
        {
            return match.Application is not null &&
                   match.MatchingConfidence < ManualReviewMatchingThreshold;
        }

        if (parsed.Category == EmailCategory.ApplicationReceived && !parsed.ActionRequired)
        {
            if (string.Equals(parsed.Company, ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return match.Application is not null &&
                   match.MatchingConfidence < ManualReviewMatchingThreshold;
        }

        return parsed.ActionRequired ||
               parsed.Category != EmailCategory.GenericUpdate ||
               match.Application is not null;
    }

    private static bool ShouldAutoAcceptApplicationReceipt(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match)
    {
        return parsed.Category == EmailCategory.ApplicationReceived &&
               !parsed.ActionRequired &&
               (match.Application is null ||
                match.MatchingConfidence >= ManualReviewMatchingThreshold);
    }
}

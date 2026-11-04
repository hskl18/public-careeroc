using Careeroc.Application.Models;
using Careeroc.Contracts.Agents;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services;

public static class AgentTraceSummaryBuilder
{
    public static AgentTraceSummaryDto FromInboxEmail(InboxEmail email)
    {
        return FromInboxEmailTrace(
            email.Id,
            email.ThreadId,
            email.ClassificationConfidence,
            email.RequiresManualReview,
            email.ProcessingTraceJson);
    }

    public static AgentTraceSummaryDto FromInboxEmailTrace(
        Guid emailId,
        string threadId,
        decimal? classificationConfidence,
        bool requiresManualReview,
        string? processingTraceJson)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(processingTraceJson);
        var workflowStep = FindStep(trace, "workflow_extraction");
        var reviewStep = FindStep(trace, "review_evidence");
        var parsingSource = FindFact(workflowStep, "Parsing source");
        var parsingModel = FindFact(workflowStep, "Parsing model");
        var confidence = ParseDecimal(FindFact(workflowStep, "Classification confidence")) ??
                         classificationConfidence;
        var reviewGateResult = ResolveReviewGateResult(requiresManualReview, reviewStep);

        return new AgentTraceSummaryDto(
            string.IsNullOrWhiteSpace(parsingModel) ? null : parsingModel,
            workflowStep?.Stage ?? "workflow_extraction",
            confidence,
            ResolveEvidenceSource(emailId, threadId, parsingSource, workflowStep),
            reviewGateResult,
            ResolveFallbackPath(parsingSource, parsingModel, requiresManualReview));
    }

    public static AgentTraceSummaryDto FromArtifactEvidence(ApplicationArtifactEvidence evidence)
    {
        return new AgentTraceSummaryDto(
            evidence.ModelPath,
            evidence.Purpose,
            evidence.Confidence,
            evidence.EvidenceSource,
            evidence.ReviewGateResult,
            evidence.FallbackPath);
    }

    private static string ResolveEvidenceSource(
        Guid emailId,
        string threadId,
        string? parsingSource,
        AgentTraceStep? workflowStep)
    {
        if (!string.IsNullOrWhiteSpace(parsingSource))
        {
            return parsingSource;
        }

        if (!string.IsNullOrWhiteSpace(workflowStep?.AgentName))
        {
            return workflowStep.AgentName;
        }

        return string.IsNullOrWhiteSpace(threadId)
            ? $"gmail_message:{emailId:N}"
            : $"gmail_thread:{threadId}";
    }

    private static string ResolveReviewGateResult(bool requiresManualReview, AgentTraceStep? reviewStep)
    {
        var reviewFact = FindFact(reviewStep, "Requires manual review");
        if (!string.IsNullOrWhiteSpace(reviewFact))
        {
            return reviewFact.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                ? "manual_review_required"
                : "cleared";
        }

        return requiresManualReview ? "manual_review_required" : "cleared";
    }

    private static string ResolveFallbackPath(string? parsingSource, string? parsingModel, bool requiresManualReview)
    {
        if (requiresManualReview)
        {
            return "manual_review_no_mutation";
        }

        if (string.IsNullOrWhiteSpace(parsingModel))
        {
            return string.IsNullOrWhiteSpace(parsingSource)
                ? "deterministic_parser"
                : parsingSource;
        }

        return "deterministic_parser_if_model_unavailable";
    }

    private static AgentTraceStep? FindStep(InboxEmailAgentTrace? trace, string stage)
    {
        return trace?.Steps.FirstOrDefault(
            step => string.Equals(step.Stage, stage, StringComparison.Ordinal));
    }

    private static string? FindFact(AgentTraceStep? step, string label)
    {
        return step?.Facts.FirstOrDefault(
            fact => string.Equals(fact.Label, label, StringComparison.Ordinal))?.Value;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, out var parsed) ? parsed : null;
    }
}

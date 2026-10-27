using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ReviewEvidenceAgentServiceTests
{
    [Fact]
    public async Task EvaluateAsync_Escalates_WhenGemmaSecondPassFindsRisk()
    {
        var service = new ReviewEvidenceAgentService(
            new StubReviewEvidenceModel(
                new ReviewEvidenceModelDecision(
                    true,
                    "Offer deadline needs a clearer evidence check.",
                    "Gemma review agent escalated the offer workflow.",
                    "Review Evidence Agent: Gemma via Ollama (gemma4:26b)",
                    0.88m)));
        var parsed = BuildParsed(EmailCategory.Offer, "Figma", "Product Engineer", actionRequired: true);
        var match = BuildMatch("Figma", "Product Engineer", 0.92m);

        var decision = await service.EvaluateAsync(parsed, match);

        Assert.True(decision.RequiresManualReview);
        Assert.Equal("Offer deadline needs a clearer evidence check.", decision.Reason);
        Assert.Equal("Review Evidence Agent: Gemma via Ollama (gemma4:26b)", decision.AgentName);
    }

    [Fact]
    public async Task EvaluateAsync_AllowsHighConfidenceGemmaClear_ForKnownEntities()
    {
        var service = new ReviewEvidenceAgentService(
            new StubReviewEvidenceModel(
                new ReviewEvidenceModelDecision(
                    false,
                    null,
                    "Gemma review agent cleared a known-company offer after second-pass evidence review.",
                    "Review Evidence Agent: Gemma via Ollama (gemma4:31b)",
                    0.91m)));
        var parsed = BuildParsed(EmailCategory.Offer, "Northgate Physics", "Offer Letter / Hire Paperwork", actionRequired: true);
        var match = BuildMatch("Northgate Physics", "Offer Letter / Hire Paperwork", 0.42m, "Existing application match was weak.");

        var decision = await service.EvaluateAsync(parsed, match);

        Assert.False(decision.RequiresManualReview);
        Assert.Null(decision.Reason);
        Assert.Contains("Gemma review agent cleared", decision.VisibleOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotLetGemmaClearUnknownRoleEscalation()
    {
        var service = new ReviewEvidenceAgentService(
            new StubReviewEvidenceModel(
                new ReviewEvidenceModelDecision(
                    false,
                    null,
                    "Gemma review agent cleared the workflow.",
                    "Review Evidence Agent: Gemma via Ollama (gemma4:31b)",
                    0.99m)));
        var parsed = BuildParsed(EmailCategory.Offer, "Northgate Physics", ApplicationDefaults.UnknownRole, actionRequired: true);
        var match = BuildMatch("Northgate Physics", "Teaching Assistant", 0.93m);

        var decision = await service.EvaluateAsync(parsed, match);

        Assert.True(decision.RequiresManualReview);
        Assert.Equal("Review Evidence Agent", decision.AgentName);
        Assert.Contains("Role could not be extracted confidently.", decision.Reason, StringComparison.Ordinal);
    }

    private static ParsedRecruitingEmail BuildParsed(
        EmailCategory category,
        string company,
        string role,
        bool actionRequired)
    {
        return new ParsedRecruitingEmail(
            category,
            category == EmailCategory.Offer ? ApplicationStatus.Offer : ApplicationStatus.Applied,
            company,
            role,
            actionRequired,
            DueDateUtc: null,
            ClassificationConfidence: 0.93m,
            WorkflowNote: "test workflow note",
            ParsingSource: "test",
            ParsingModel: null);
    }

    private static ApplicationMatchResult BuildMatch(
        string company,
        string role,
        decimal confidence,
        string? reviewReason = null)
    {
        return new ApplicationMatchResult(
            new JobApplication
            {
                Company = company,
                Role = role,
                Status = ApplicationStatus.Applied
            },
            Created: false,
            MatchingConfidence: confidence,
            ReviewReason: reviewReason);
    }

    private sealed class StubReviewEvidenceModel(ReviewEvidenceModelDecision? decision) : IReviewEvidenceModel
    {
        public Task<ReviewEvidenceModelDecision?> EvaluateAsync(
            ParsedRecruitingEmail parsed,
            ApplicationMatchResult match,
            ReviewEvidenceDecision baselineDecision,
            CancellationToken cancellationToken = default,
            ModelCallContext? modelCallContext = null)
        {
            return Task.FromResult(decision);
        }
    }
}

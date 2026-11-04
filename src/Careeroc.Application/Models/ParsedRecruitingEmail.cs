using Careeroc.Domain.Enums;

namespace Careeroc.Application.Models;

public sealed record ParsedRecruitingEmail(
    EmailCategory Category,
    ApplicationStatus RecommendedStatus,
    string Company,
    string Role,
    bool ActionRequired,
    DateTimeOffset? DueDateUtc,
    decimal ClassificationConfidence,
    string WorkflowNote,
    string ParsingSource,
    string? ParsingModel);

using Careeroc.Domain.Enums;

namespace Careeroc.Application.Models;

public sealed record RecruiterIdentityDecision(
    string AgentName,
    string? ContactName,
    string? ContactEmail,
    string? RoleHint,
    RecruitingContactType? ContactType,
    decimal Confidence,
    string VisibleOutput,
    string? Reason);

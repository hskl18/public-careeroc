using Careeroc.Domain.Enums;

namespace Careeroc.Application.Models;

public sealed record RecruitingEmailGuidanceFeedback(
    DateTimeOffset RecordedAtUtc,
    string Sender,
    string Subject,
    EmailCategory? PreviousCategory,
    EmailCategory? CorrectedCategory,
    bool PreviousActionRequired,
    bool CorrectedActionRequired,
    DateTimeOffset? PreviousDueDateUtc,
    DateTimeOffset? CorrectedDueDateUtc,
    string? PreviousCompany,
    string? CorrectedCompany,
    string? PreviousRole,
    string? CorrectedRole,
    string? Notes);

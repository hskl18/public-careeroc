using Careeroc.Domain.Entities;

namespace Careeroc.Application.Models;

public sealed record ApplicationMatchResult(
    JobApplication? Application,
    bool Created,
    decimal MatchingConfidence,
    string? ReviewReason);

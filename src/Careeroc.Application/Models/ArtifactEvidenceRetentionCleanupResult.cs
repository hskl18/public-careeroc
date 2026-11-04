namespace Careeroc.Application.Models;

public sealed record ArtifactEvidenceRetentionCleanupResult(
    int RedactedArtifactEvidence,
    DateTimeOffset? CutoffUtc);

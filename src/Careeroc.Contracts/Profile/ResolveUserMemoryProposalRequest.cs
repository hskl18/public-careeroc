namespace Careeroc.Contracts.Profile;

public sealed record ResolveUserMemoryProposalRequest(
    string Action,
    DateTimeOffset? ExpectedUpdatedAtUtc = null);

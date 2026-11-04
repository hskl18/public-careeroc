namespace Careeroc.Contracts.Applications;

public sealed class CorrectApplicationRequest
{
    public string Kind { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

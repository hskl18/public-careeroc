namespace Careeroc.Contracts.Applications;

public sealed class UpdateApplicationStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public int? Priority { get; init; }
}

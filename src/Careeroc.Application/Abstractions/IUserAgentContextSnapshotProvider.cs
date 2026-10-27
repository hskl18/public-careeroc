using Careeroc.Contracts.Profile;

namespace Careeroc.Application.Abstractions;

public interface IUserAgentContextSnapshotProvider
{
    Task<UserAgentContextSnapshotDto> GetSnapshotAsync(
        string? userEmail,
        CancellationToken cancellationToken = default);
}

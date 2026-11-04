using Careeroc.Domain;

namespace Careeroc.Api.Security;

internal sealed record AuthenticatedUserContext(
    string Email,
    string? Name,
    string Role = UserRoleNames.User);

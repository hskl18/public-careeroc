using Careeroc.Api.Security;
using Careeroc.Domain;

namespace Careeroc.Api.Endpoints;

internal static class RequestUserContext
{
    private const string UserContextKey = "__CareerocAuthenticatedUser";

    public static string? GetUserEmail(HttpRequest request)
    {
        return GetUserContext(request.HttpContext)?.Email;
    }

    public static string? GetUserName(HttpRequest request)
    {
        return GetUserContext(request.HttpContext)?.Name;
    }

    public static string GetUserRole(HttpRequest request)
    {
        return GetUserContext(request.HttpContext)?.Role ?? UserRoleNames.User;
    }

    public static bool IsAdmin(HttpRequest request)
    {
        return UserRoleNames.IsAdmin(GetUserRole(request));
    }

    public static void SetUserContext(HttpContext httpContext, AuthenticatedUserContext userContext)
    {
        httpContext.Items[UserContextKey] = userContext;
    }

    private static AuthenticatedUserContext? GetUserContext(HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue(UserContextKey, out var value)
            ? value as AuthenticatedUserContext
            : null;
    }
}

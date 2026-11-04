namespace Careeroc.Api.Endpoints;

internal static class AdminAuthorization
{
    public static bool RequireAdmin(HttpRequest request)
    {
        return RequestUserContext.IsAdmin(request);
    }
}

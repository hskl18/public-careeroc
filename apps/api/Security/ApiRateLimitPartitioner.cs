using Careeroc.Api.Endpoints;

namespace Careeroc.Api.Security;

internal static class ApiRateLimitPartitioner
{
    public static string ResolvePartitionKey(HttpContext context)
    {
        var userEmail = RequestUserContext.GetUserEmail(context.Request);
        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            return $"user:{userEmail.Trim().ToLowerInvariant()}";
        }

        return $"ip:{GetRemoteIp(context)}";
    }

    public static string GetRemoteIp(HttpContext context)
    {
        return string.IsNullOrWhiteSpace(context.Connection.RemoteIpAddress?.ToString())
            ? "unknown"
            : context.Connection.RemoteIpAddress!.ToString();
    }
}

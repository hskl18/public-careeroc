using System.Net;

namespace Careeroc.Api.Security;

// Public URLs need HTTPS (loopback allowed for local containers); service-to-service URLs
// may use plain HTTP inside a private network such as Docker, Kubernetes, or Railway.
public static class ProductionUrlPolicy
{
    private static readonly string[] PrivateHostSuffixes = [".internal", ".local", ".cluster.local"];

    public static bool IsAllowed(Uri uri, bool isInternal)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp &&
               (isInternal ? IsPrivateNetworkHost(uri.Host) : uri.IsLoopback);
    }

    private static bool IsPrivateNetworkHost(string host)
    {
        return IPAddress.TryParse(host.Trim('[', ']'), out var address)
            ? IPAddress.IsLoopback(address)
            : !host.Contains('.') ||
              PrivateHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ||
              host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}

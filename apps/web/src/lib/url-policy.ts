// Production URL rules: public URLs need HTTPS (loopback allowed for local containers);
// service-to-service URLs may use plain HTTP inside a private network.
const privateHostSuffixes = [".internal", ".local", ".cluster.local"];

function isLoopbackHost(hostname: string) {
  const host = hostname.replace(/^\[|\]$/g, "").toLowerCase();
  return host === "localhost" || host === "::1" || /^127(?:\.\d{1,3}){3}$/.test(host);
}

function isPrivateNetworkHost(hostname: string) {
  const host = hostname.toLowerCase();
  return (
    isLoopbackHost(host) ||
    !host.includes(".") ||
    privateHostSuffixes.some((suffix) => host.endsWith(suffix))
  );
}

export function isAllowedProductionUrl(url: URL, scope: "public" | "internal") {
  if (url.protocol === "https:") {
    return true;
  }

  return (
    url.protocol === "http:" &&
    (scope === "internal" ? isPrivateNetworkHost(url.hostname) : isLoopbackHost(url.hostname))
  );
}

// Redirect targets read from a query string or an upstream header: only workspace paths on this
// site. Checking for a leading "/" is not enough, since browsers read "/\\evil.com" as "//evil.com".
export function safeAppPath(value: string | null | undefined, fallback = "/app") {
  return value && /^\/app(?:[/?#]|$)/.test(value) ? value : fallback;
}

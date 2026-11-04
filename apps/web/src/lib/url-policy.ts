// Production URL rules: public URLs need HTTPS (loopback allowed for local containers);
// service-to-service URLs may use plain HTTP inside a private network.
const privateHostSuffixes = [".internal", ".local", ".cluster.local"];

export function isLoopbackHost(hostname: string) {
  const host = hostname.replace(/^\[|\]$/g, "").toLowerCase();
  return host === "localhost" || host === "::1" || /^127(?:\.\d{1,3}){3}$/.test(host);
}

export function isPrivateNetworkHost(hostname: string) {
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

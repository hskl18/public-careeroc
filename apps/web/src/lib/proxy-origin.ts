import type { NextRequest } from "next/server";

let warnedAboutMissingProductionOrigin = false;

// Every current browser sets Sec-Fetch-Site, and a page cannot forge it. No proxied route is meant
// to be opened from another site, and GET .../gmail/oauth/start writes, so a cross-site navigation,
// which still carries the Lax session cookie, is refused whatever its method.
export function isCrossSiteRequest(headers: Headers) {
  const site = headers.get("sec-fetch-site");
  return site !== null && site !== "same-origin" && site !== "none";
}

// Next decodes each catch-all segment, so "..%2F" arrives as "../". Segments are encoded again and
// the path normalized the way fetch will send it; anything that resolves outside /api/ is refused.
export function resolveProxyPath(segments: string[], search: string): string | null {
  const target = new URL(
    `/${segments.map(encodeURIComponent).join("/")}${search}`,
    "http://careeroc.invalid",
  );
  return target.pathname.startsWith("/api/") ? `${target.pathname}${target.search}` : null;
}

export function hasTrustedBrowserOrigin(request: NextRequest) {
  if (isProduction() && !hasConfiguredProductionOrigin()) {
    warnMissingProductionOrigin();
    return false;
  }

  const trustedOrigins = getTrustedBrowserOrigins(request);
  const origin = request.headers.get("origin");

  if (origin) {
    const normalizedOrigin = normalizeOrigin(origin);
    return normalizedOrigin ? trustedOrigins.has(normalizedOrigin) : false;
  }

  const referer = request.headers.get("referer");
  if (!referer) {
    return process.env.NODE_ENV !== "production";
  }

  try {
    const normalizedRefererOrigin = normalizeOrigin(new URL(referer).origin);
    return normalizedRefererOrigin
      ? trustedOrigins.has(normalizedRefererOrigin)
      : false;
  } catch {
    return false;
  }
}

function getTrustedBrowserOrigins(request: NextRequest) {
  const origins = new Set<string>();
  addTrustedOrigin(origins, process.env.AUTH_URL);
  addTrustedOrigin(origins, process.env.NEXTAUTH_URL);
  addTrustedOrigin(origins, process.env.NEXT_PUBLIC_SITE_URL);

  if (process.env.NODE_ENV !== "production") {
    addTrustedOrigin(origins, request.nextUrl.origin);
    addTrustedOrigin(origins, "http://localhost:3000");
    addTrustedOrigin(origins, "http://127.0.0.1:3000");
  }

  return origins;
}

function normalizeOrigin(value?: string | null) {
  const trimmed = value?.trim();
  if (!trimmed) {
    return null;
  }

  try {
    return new URL(trimmed).origin;
  } catch {
    return null;
  }
}

function addTrustedOrigin(origins: Set<string>, value?: string | null) {
  const origin = normalizeOrigin(value);
  if (origin) {
    origins.add(origin);
  }
}

function hasConfiguredProductionOrigin() {
  return Boolean(
    normalizeOrigin(process.env.AUTH_URL) ??
      normalizeOrigin(process.env.NEXTAUTH_URL) ??
      normalizeOrigin(process.env.NEXT_PUBLIC_SITE_URL),
  );
}

function isProduction() {
  return process.env.NODE_ENV === "production";
}

function warnMissingProductionOrigin() {
  if (warnedAboutMissingProductionOrigin) {
    return;
  }

  warnedAboutMissingProductionOrigin = true;
  console.error(
    "[proxy-origin] Production browser-origin checks require AUTH_URL, NEXTAUTH_URL, or NEXT_PUBLIC_SITE_URL. Mutating browser requests will be rejected.",
  );
}

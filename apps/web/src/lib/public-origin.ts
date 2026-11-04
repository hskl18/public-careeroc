import type { NextRequest } from "next/server";

// Behind a container or proxy, request.url carries the bind address (for example
// http://0.0.0.0:3000), so redirects use the configured public origin when there is one.
export function resolvePublicOrigin(request: NextRequest) {
  const configuredUrl = process.env.AUTH_URL ?? process.env.NEXTAUTH_URL;
  if (configuredUrl) {
    try {
      return new URL(configuredUrl).origin;
    } catch {}
  }

  return request.nextUrl.origin;
}

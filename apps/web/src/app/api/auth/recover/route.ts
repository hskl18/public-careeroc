import { NextResponse, type NextRequest } from "next/server";
import { appendExpiredAuthCookies } from "@/lib/auth-cookie-cleanup";
import { resolvePublicOrigin } from "@/lib/public-origin";
import { safeAppPath } from "@/lib/url-policy";

export function GET(request: NextRequest) {
  const nextPath = safeAppPath(request.nextUrl.searchParams.get("next"));
  const redirectUrl = new URL("/", resolvePublicOrigin(request));

  redirectUrl.searchParams.set("next", nextPath);
  redirectUrl.searchParams.set("session", "recovered");

  const response = NextResponse.redirect(redirectUrl);
  appendExpiredAuthCookies(response.headers);

  return response;
}

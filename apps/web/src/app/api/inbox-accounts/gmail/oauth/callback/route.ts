import { NextResponse, type NextRequest } from "next/server";
import { auth } from "@/auth";
import { fetchCareerocApi } from "@/lib/careeroc-api-server";
import { resolvePublicOrigin } from "@/lib/public-origin";

export const runtime = "nodejs";

// Google sends the browser back here. The callback reaches the API signed as the signed-in user,
// and the API only completes a Gmail connection that this same user started.
export async function GET(request: NextRequest) {
  const origin = resolvePublicOrigin(request);
  const session = await auth();
  const email = session?.user?.email?.trim();
  if (!email) {
    return NextResponse.redirect(new URL("/?next=/app/inbox", origin));
  }

  let upstream: Response;
  try {
    upstream = await fetchCareerocApi(
      `/api/inbox-accounts/gmail/oauth/callback${request.nextUrl.search}`,
      { email, name: session?.user?.name ?? null },
      { cache: "no-store", redirect: "manual" },
    );
  } catch (error) {
    console.warn(
      "[gmail-oauth-callback] upstream request failed",
      error instanceof Error ? error.message : error,
    );
    return NextResponse.redirect(new URL("/app/inbox?gmail=upstream_unavailable", origin));
  }

  // The API answers with a path on this site; anything else lands on the inbox page.
  const location = upstream.headers.get("location");
  const target =
    location?.startsWith("/") && !location.startsWith("//") ? location : "/app/inbox#mail-sync";
  return NextResponse.redirect(new URL(target, origin));
}

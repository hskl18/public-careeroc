import { NextResponse, type NextRequest } from "next/server";
import { appendExpiredAuthCookies } from "@/lib/auth-cookie-cleanup";
import { resolvePublicOrigin } from "@/lib/public-origin";

export function POST(request: NextRequest) {
  return redirectWithClearedAuth(request);
}

export function GET() {
  return NextResponse.json({ error: "Method not allowed." }, { status: 405 });
}

function redirectWithClearedAuth(request: NextRequest) {
  const response = shouldReturnJson(request)
    ? NextResponse.json({ ok: true })
    : NextResponse.redirect(new URL("/", resolvePublicOrigin(request)), {
        status: 303,
      });

  appendExpiredAuthCookies(response.headers);
  response.headers.set("Cache-Control", "no-store");
  return response;
}

function shouldReturnJson(request: NextRequest) {
  return request.headers.get("accept")?.includes("application/json") ?? false;
}

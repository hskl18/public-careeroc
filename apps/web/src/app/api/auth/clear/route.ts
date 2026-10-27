import { NextResponse, type NextRequest } from "next/server";
import { appendExpiredAuthCookies } from "@/lib/auth-cookie-cleanup";
import { resolvePublicOrigin } from "@/lib/public-origin";

const isProduction = process.env.NODE_ENV === "production";

export function GET(request: NextRequest) {
  if (isProduction) {
    return NextResponse.json({ error: "Not found." }, { status: 404 });
  }

  const response = NextResponse.redirect(new URL("/", resolvePublicOrigin(request)));
  appendExpiredAuthCookies(response.headers);

  return response;
}

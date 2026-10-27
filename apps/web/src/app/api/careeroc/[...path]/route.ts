import { NextResponse, type NextRequest } from "next/server";
import { auth, isAuthEnabled, isUserAccessAllowed } from "@/auth";
import {
  buildCareerocApiAuthHeaders,
  getCareerocBackendBaseUrl,
} from "@/lib/careeroc-api-server";
import { hasTrustedBrowserOrigin } from "@/lib/proxy-origin";
import { readBodyWithin } from "@/lib/read-body";

export const runtime = "nodejs";

type RouteContext = {
  params: Promise<{
    path: string[];
  }>;
};

export async function GET(request: NextRequest, context: RouteContext) {
  return proxyCareerocRequest(request, context);
}

export async function POST(request: NextRequest, context: RouteContext) {
  return proxyCareerocRequest(request, context);
}

export async function PATCH(request: NextRequest, context: RouteContext) {
  return proxyCareerocRequest(request, context);
}

export async function DELETE(request: NextRequest, context: RouteContext) {
  return proxyCareerocRequest(request, context);
}

const mutatingMethods = new Set(["POST", "PATCH", "DELETE"]);
const defaultMaxProxyBodyBytes = 8_388_608;
const hardMaxProxyBodyBytes = 10_485_760;
const maxProxyBodyBytes = resolveMaxProxyBodyBytes();

async function proxyCareerocRequest(
  request: NextRequest,
  context: RouteContext,
) {
  if (!isAuthEnabled) {
    return NextResponse.json(
      { error: "careeroc web authentication is not configured." },
      { status: 503 },
    );
  }

  const session = await auth();
  const userEmail = session?.user?.email?.trim();
  const userName = session?.user?.name?.trim() || null;
  if (!userEmail) {
    return NextResponse.json({ error: "Authentication required." }, { status: 401 });
  }

  if (!isUserAccessAllowed(userEmail)) {
    return NextResponse.json(
      { error: "User is not allowed to access this workspace." },
      { status: 403 },
    );
  }

  if (mutatingMethods.has(request.method) && !hasTrustedBrowserOrigin(request)) {
    return NextResponse.json({ error: "Invalid request origin." }, { status: 403 });
  }

  const apiBaseUrl = getCareerocBackendBaseUrl();
  if (!apiBaseUrl) {
    return NextResponse.json(
      { error: "careeroc API base URL is not configured." },
      { status: 503 },
    );
  }

  const { path } = await context.params;
  if (path.length === 0 || path[0] !== "api") {
    return NextResponse.json({ error: "Unsupported careeroc path." }, { status: 404 });
  }

  const pathAndQuery = `/${path.join("/")}${request.nextUrl.search}`;
  const declaredLength = Number(request.headers.get("content-length") ?? 0);
  if (!Number.isFinite(declaredLength) || declaredLength < 0) {
    return NextResponse.json({ error: "Invalid Content-Length." }, { status: 400 });
  }
  if (declaredLength > maxProxyBodyBytes) {
    return NextResponse.json({ error: "Request body is too large." }, { status: 413 });
  }

  const requestBody =
    request.method === "GET" || request.method === "HEAD"
      ? undefined
      : await readBodyWithin(request, maxProxyBodyBytes);
  if (requestBody === null) {
    return NextResponse.json({ error: "Request body is too large." }, { status: 413 });
  }
  const requestBodyForSignature = requestBody?.toString("utf8") ?? "";

  const headers = buildCareerocApiAuthHeaders(
    {
      email: userEmail,
      name: userName,
    },
    {
      method: request.method,
      pathAndQuery,
      body: requestBodyForSignature,
    },
  );

  const accept = request.headers.get("accept");
  if (accept) {
    headers.set("accept", accept);
  }

  const contentType = request.headers.get("content-type");
  if (contentType) {
    headers.set("content-type", contentType);
  }

  let upstream: Response;
  try {
    upstream = await fetch(`${apiBaseUrl}${pathAndQuery}`, {
      method: request.method,
      headers,
      body: requestBody,
      cache: "no-store",
      redirect: "manual",
    });
  } catch (error) {
    console.warn(
      "[careeroc-proxy] upstream request failed",
      error instanceof Error ? error.message : error,
    );
    return NextResponse.json(
      { error: "careeroc API is unavailable. Start the API server and retry." },
      { status: 503 },
    );
  }

  const location = upstream.headers.get("location");
  if (location && upstream.status >= 300 && upstream.status < 400) {
    return NextResponse.redirect(
      new URL(location, request.nextUrl.origin),
      upstream.status,
    );
  }

  const responseHeaders = new Headers();
  const responseContentType = upstream.headers.get("content-type");
  if (responseContentType) {
    responseHeaders.set("content-type", responseContentType);
  }

  const cacheControl = upstream.headers.get("cache-control");
  responseHeaders.set("cache-control", cacheControl ?? "no-store");

  const retryAfter = upstream.headers.get("retry-after");
  if (retryAfter) {
    responseHeaders.set("retry-after", retryAfter);
  }

  if (upstream.status === 204) {
    return new NextResponse(null, {
      status: upstream.status,
      headers: responseHeaders,
    });
  }

  if (upstream.status === 413) {
    console.warn(
      "[careeroc-proxy] upstream rejected request body size",
      `proxyLimit=${maxProxyBodyBytes}`,
      `path=${pathAndQuery}`,
    );
  }

  return new NextResponse(upstream.body, {
    status: upstream.status,
    headers: responseHeaders,
  });
}

function resolveMaxProxyBodyBytes() {
  const configured =
    process.env.CAREEROC_PROXY_MAX_BODY_BYTES ??
    process.env.SECURITY_MAX_REQUEST_BODY_BYTES;
  const parsed = Number(configured);

  if (!Number.isFinite(parsed) || parsed <= 0) {
    return defaultMaxProxyBodyBytes;
  }

  return Math.min(Math.floor(parsed), hardMaxProxyBodyBytes);
}

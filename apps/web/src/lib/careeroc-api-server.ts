import "server-only";

import { createHash, createHmac } from "node:crypto";
import type { CareerocApiUser } from "./api-user";
import { isAllowedProductionUrl } from "./url-policy";

const clientApiBaseUrl = "/api/careeroc";
const isProduction = process.env.NODE_ENV === "production";
const devApiAuthSecret = "careeroc-dev-api-auth-secret-change-me";
const devAuthSecret = "careeroc-dev-auth-secret-change-me";
const minimumSecretLength = 32;

export function getCareerocBackendBaseUrl() {
  return normalizeBaseUrl(process.env.API_BASE_URL, "API_BASE_URL");
}

export function getCareerocClientApiBaseUrl() {
  return clientApiBaseUrl;
}

export function buildCareerocApiAuthHeaders(
  user: CareerocApiUser,
  request: {
    method: string;
    pathAndQuery: string;
    body?: string | null;
  },
  initHeaders?: HeadersInit,
) {
  const method = request.method.toUpperCase();
  const timestamp = new Date().toISOString();
  const normalizedEmail = normalizeEmail(user.email);
  const normalizedName = normalizeName(user.name);
  const bodyHash = computeBodyHash(request.body ?? "");
  const headers = new Headers(initHeaders);

  headers.set("X-Careeroc-User-Email", normalizedEmail);
  if (normalizedName) {
    headers.set("X-Careeroc-User-Name", normalizedName);
  } else {
    headers.delete("X-Careeroc-User-Name");
  }

  headers.set("X-Careeroc-Timestamp", timestamp);
  headers.set("X-Careeroc-Body-SHA256", bodyHash);
  headers.set(
    "X-Careeroc-Signature",
    computeSignature(
      resolveSecret(),
      method,
      request.pathAndQuery,
      normalizedEmail,
      normalizedName,
      timestamp,
      bodyHash,
    ),
  );

  return headers;
}

export async function fetchCareerocApi(
  pathAndQuery: string,
  user: CareerocApiUser,
  init?: RequestInit,
) {
  const backendBaseUrl = getCareerocBackendBaseUrl();
  if (!backendBaseUrl) {
    throw new Error("careeroc API base URL is not configured.");
  }

  const method = init?.method?.toUpperCase() ?? "GET";
  const body = typeof init?.body === "string" ? init.body : null;
  const headers = buildCareerocApiAuthHeaders(user, { method, pathAndQuery, body }, init?.headers);

  return fetch(`${backendBaseUrl}${pathAndQuery}`, {
    ...init,
    method,
    headers,
  });
}

function resolveSecret() {
  const configured = process.env.CAREEROC_API_AUTH_SECRET;

  if (configured?.trim()) {
    const secret = configured.trim();

    if (isProduction) {
      assertStrongSecret(secret);
    }

    return secret;
  }

  if (!isProduction) {
    return devApiAuthSecret;
  }

  throw new Error(
    "careeroc API auth secret is not configured. Set CAREEROC_API_AUTH_SECRET.",
  );
}

function normalizeBaseUrl(value?: string | null, variableName = "base URL") {
  const trimmed = value?.trim();
  if (!trimmed) {
    return null;
  }

  const normalized = trimmed.replace(/\/+$/, "");
  if (isProduction) {
    let parsed: URL;
    try {
      parsed = new URL(normalized);
    } catch {
      throw new Error(`${variableName} must be an absolute HTTPS URL in production.`);
    }

    if (!isAllowedProductionUrl(parsed, "internal")) {
      throw new Error(
        `${variableName} must use HTTPS in production unless it points at a private-network host.`,
      );
    }
  }

  return normalized;
}

function normalizeEmail(value: string) {
  return value.trim().toLowerCase();
}

function normalizeName(value?: string | null) {
  return value?.trim() ?? "";
}

function computeBodyHash(body: string) {
  return createHash("sha256").update(body, "utf8").digest("hex");
}

function computeSignature(
  secret: string,
  method: string,
  pathAndQuery: string,
  email: string,
  name: string,
  timestamp: string,
  bodyHash: string,
) {
  const payload = [method, pathAndQuery, email, name, timestamp, bodyHash].join("\n");
  return createHmac("sha256", secret).update(payload).digest("hex");
}

function assertStrongSecret(secret: string) {
  if (
    secret === devApiAuthSecret ||
    secret === devAuthSecret ||
    secret.length < minimumSecretLength
  ) {
    throw new Error(
      "careeroc API auth secret must be at least 32 characters and must not use development defaults in production.",
    );
  }
}

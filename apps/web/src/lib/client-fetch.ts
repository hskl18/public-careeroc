const inFlightRequests = new Map<string, Promise<Response>>();

function getRequestKey(input: RequestInfo | URL, init?: RequestInit) {
  const method = init?.method?.toUpperCase() ?? "GET";
  if (method !== "GET") return null;

  const url =
    typeof input === "string"
      ? input
      : input instanceof URL
        ? input.toString()
        : input.url;

  return `${method}:${url}`;
}

export async function fetchWithInFlightDedup(
  input: RequestInfo | URL,
  init?: RequestInit,
  options?: { dedupeKey?: string },
) {
  const key = options?.dedupeKey ?? getRequestKey(input, init);
  if (!key) {
    return fetch(input, init);
  }

  const existing = inFlightRequests.get(key);
  if (existing) {
    return existing.then((response) => response.clone());
  }

  const request = fetch(input, init).finally(() => {
    inFlightRequests.delete(key);
  });
  inFlightRequests.set(key, request);

  return request.then((response) => response.clone());
}

// The API answers errors as { error } (the demo's read-only notice among them); fall back to the
// status when the body has no message.
export async function readErrorMessage(response: Response) {
  const payload = (await response.json().catch(() => null)) as { error?: string } | null;
  return payload?.error || `Request failed with ${response.status}`;
}

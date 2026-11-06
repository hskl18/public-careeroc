import { describe, expect, it } from "vitest";

import { isAllowedProductionUrl, safeAppPath } from "./url-policy";

describe("production URL policy", () => {
  it.each([
    ["https://api.careeroc.com", "public", true],
    ["http://localhost:5114", "public", true],
    ["http://api.careeroc.com", "public", false],
    ["http://api:8080", "public", false],
    ["http://api:8080", "internal", true],
    ["http://careeroc-api.railway.internal:8080", "internal", true],
    ["http://api.careeroc.svc.cluster.local", "internal", true],
    ["http://api.careeroc.com", "internal", false],
    ["ftp://api", "internal", false],
  ] as const)("%s as %s -> %s", (value, scope, allowed) => {
    expect(isAllowedProductionUrl(new URL(value), scope)).toBe(allowed);
  });
});

describe("safeAppPath", () => {
  const origin = "https://careeroc.example";

  it.each([
    ["/app/applications?view=all", "/app/applications?view=all"],
    ["/app", "/app"],
    ["/app#mail-sync", "/app#mail-sync"],
    ["/\\evil.com", "/app"],
    ["//evil.com", "/app"],
    ["https://evil.com/app", "/app"],
    ["/apple", "/app"],
    ["", "/app"],
    [null, "/app"],
  ] as const)("%s -> %s", (value, expected) => {
    expect(safeAppPath(value)).toBe(expected);
  });

  it("never leaves the site, whatever the browser does with the path", () => {
    for (const value of ["/\\evil.com", "//evil.com", "/app/\\evil.com", "/app/../..//evil.com"]) {
      const url = new URL(safeAppPath(value), origin);
      expect(url.origin).toBe(origin);
    }
  });

  it("uses the given fallback", () => {
    expect(safeAppPath("https://evil.com", "/app/inbox#mail-sync")).toBe("/app/inbox#mail-sync");
  });
});

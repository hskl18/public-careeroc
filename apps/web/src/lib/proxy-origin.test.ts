import { describe, expect, it } from "vitest";
import { isCrossSiteRequest, resolveProxyPath } from "./proxy-origin";

describe("resolveProxyPath", () => {
  it("keeps an API path and its query", () => {
    expect(resolveProxyPath(["api", "dashboard"], "?view=all")).toBe("/api/dashboard?view=all");
  });

  it("keeps a decoded ../ inside its own segment, so it cannot climb out of /api", () => {
    expect(resolveProxyPath(["api", "../health"], "")).toBe("/api/..%2Fhealth");
  });

  it.each([
    ["a dot segment", ["api", "..", "mcp"]],
    ["a path outside /api", ["agent", "v1", "me"]],
    ["the bare prefix", ["api"]],
  ])("refuses %s", (_, segments) => {
    expect(resolveProxyPath(segments, "")).toBeNull();
  });

  it("keeps a decoded question mark in the path instead of starting a query", () => {
    expect(resolveProxyPath(["api", "applications", "a?b"], "")).toBe("/api/applications/a%3Fb");
  });
});

describe("isCrossSiteRequest", () => {
  it.each([
    ["cross-site", true],
    ["same-site", true],
    ["same-origin", false],
    ["none", false],
  ])("treats Sec-Fetch-Site %s as cross-site: %s", (value, expected) => {
    expect(isCrossSiteRequest(new Headers({ "sec-fetch-site": value }))).toBe(expected);
  });

  it("leaves a request from a browser without the header to the origin check", () => {
    expect(isCrossSiteRequest(new Headers())).toBe(false);
  });
});

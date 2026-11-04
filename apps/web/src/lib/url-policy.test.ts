import { describe, expect, it } from "vitest";

import { isAllowedProductionUrl } from "./url-policy";

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

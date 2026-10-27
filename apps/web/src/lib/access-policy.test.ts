import { describe, expect, it } from "vitest";

import { type AccessPolicy, isAccessAllowed } from "./access-policy";

const productionDemo: AccessPolicy = {
  allowAllUsers: false,
  allowedEmails: [],
  allowedDomains: [],
  isProduction: true,
  demoEmailDomain: "demo.careeroc.example",
};

describe("access policy", () => {
  it("lets demo identities through a production demo without an allowlist", () => {
    expect(isAccessAllowed("visitor-1@demo.careeroc.example", productionDemo)).toBe(true);
  });

  it("still rejects everyone else in that deployment", () => {
    expect(isAccessAllowed("someone@gmail.com", productionDemo)).toBe(false);
  });

  it("ignores demo addresses when demo mode is off", () => {
    expect(isAccessAllowed("visitor-1@demo.careeroc.example", { ...productionDemo, demoEmailDomain: null })).toBe(false);
  });

  it("applies the allowlist to real accounts", () => {
    const policy = { ...productionDemo, demoEmailDomain: null, allowedDomains: ["example.com"] };
    expect(isAccessAllowed("alex@example.com", policy)).toBe(true);
    expect(isAccessAllowed("alex@other.com", policy)).toBe(false);
  });

  it("is open in development when no allowlist is set", () => {
    expect(isAccessAllowed("alex@other.com", { ...productionDemo, demoEmailDomain: null, isProduction: false })).toBe(true);
  });
});

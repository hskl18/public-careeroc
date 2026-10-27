import { describe, expect, it } from "vitest";

import { createDemoIdentity, isDemoEmail } from "./demo";

describe("demo identities", () => {
  it("creates a unique visitor address under the demo domain", () => {
    const identity = createDemoIdentity("Demo.Careeroc.Example", "7F2C9A10-1B2C-4D5E-8F90-ABCDEF123456");

    expect(identity.email).toBe("visitor-7f2c9a101b2c4d5e@demo.careeroc.example");
    expect(identity.name).toBe("Alex Rivera");
    expect(isDemoEmail(identity.email, "demo.careeroc.example")).toBe(true);
  });

  it("rejects addresses outside the demo domain", () => {
    expect(isDemoEmail("visitor@demo.careeroc.example.evil.test", "demo.careeroc.example")).toBe(false);
    expect(isDemoEmail("someone@gmail.com", "demo.careeroc.example")).toBe(false);
    expect(isDemoEmail("@demo.careeroc.example", "demo.careeroc.example")).toBe(false);
    expect(isDemoEmail(null, "demo.careeroc.example")).toBe(false);
  });
});

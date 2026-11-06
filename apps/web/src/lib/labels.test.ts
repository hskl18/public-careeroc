import { describe, expect, it } from "vitest";
import { formatConfidence, formatEnumLabel } from "./labels";

describe("formatEnumLabel", () => {
  it("reads categories and statuses as words", () => {
    expect(formatEnumLabel("oa_invitation")).toBe("OA Invitation");
    expect(formatEnumLabel("Oa Pending")).toBe("OA Pending");
    expect(formatEnumLabel("interview_invitation")).toBe("Interview Invitation");
    expect(formatEnumLabel("Recruiter Contact")).toBe("Recruiter Contact");
    expect(formatEnumLabel(null)).toBe("Unclassified");
  });
});

describe("formatConfidence", () => {
  it("reads as a whole percentage", () => {
    expect(formatConfidence(0.881)).toBe("88%");
    expect(formatConfidence(1)).toBe("100%");
    expect(formatConfidence(null)).toBe("n/a");
    expect(formatConfidence(Number.NaN)).toBe("n/a");
  });
});

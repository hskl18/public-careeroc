import { describe, expect, it } from "vitest";
import { formatEnumLabel } from "./labels";

describe("formatEnumLabel", () => {
  it("reads categories and statuses as words", () => {
    expect(formatEnumLabel("oa_invitation")).toBe("OA Invitation");
    expect(formatEnumLabel("Oa Pending")).toBe("OA Pending");
    expect(formatEnumLabel("interview_invitation")).toBe("Interview Invitation");
    expect(formatEnumLabel("Recruiter Contact")).toBe("Recruiter Contact");
    expect(formatEnumLabel(null)).toBe("Unclassified");
  });
});

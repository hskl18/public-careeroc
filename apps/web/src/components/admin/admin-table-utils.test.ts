import { describe, expect, it } from "vitest";
import { buildAdminSearchText, formatUtcTimestamp } from "./admin-table-utils";

describe("admin table helpers", () => {
  it("builds normalized search text without nullish values", () => {
    expect(
      buildAdminSearchText([
        "Alex Rivera",
        "arivera@example.edu",
        12,
        null,
        undefined,
        "Needs Review",
      ]),
    ).toBe("alex rivera arivera@example.edu 12 needs review");
  });

  it("formats timestamps in UTC for operator tables", () => {
    expect(formatUtcTimestamp("2026-05-14T08:02:17.099Z")).toBe("May 14, 08:02Z");
  });

  it("keeps missing or malformed timestamps bounded", () => {
    expect(formatUtcTimestamp(null)).toBe("Never");
    expect(formatUtcTimestamp("not-a-date")).toBe("Invalid date");
  });
});

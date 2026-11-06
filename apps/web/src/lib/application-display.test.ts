import { describe, expect, it } from "vitest";
import { describeDue } from "./application-display";

describe("describeDue", () => {
  const now = new Date(2026, 10, 6, 9, 0);

  it("calls a deadline later today today, in the label and the pill alike", () => {
    expect(describeDue(new Date(2026, 10, 6, 21, 0).toISOString(), now)).toMatchObject({
      relative: "today",
      pill: { label: "Due today" },
    });
  });

  it("calls a deadline 20 hours away tomorrow and soon, not today", () => {
    expect(describeDue(new Date(2026, 10, 7, 5, 0).toISOString(), now)).toMatchObject({
      relative: "tomorrow",
      pill: { label: "Due soon" },
    });
  });

  it("marks a passed deadline overdue", () => {
    expect(describeDue(new Date(2026, 10, 5, 12, 0).toISOString(), now)).toMatchObject({
      relative: "yesterday",
      pill: { label: "Overdue" },
    });
  });

  it("drops the pill a week out and both labels for no date", () => {
    expect(describeDue(new Date(2026, 10, 13, 9, 0).toISOString(), now)).toEqual({
      relative: "in 7d",
      pill: null,
    });
    expect(describeDue(null, now)).toEqual({ relative: null, pill: null });
  });
});

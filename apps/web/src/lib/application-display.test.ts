import { describe, expect, it } from "vitest";
import { describeDue } from "./application-display";

describe("describeDue", () => {
  const zone = "America/Los_Angeles";
  // 09:00 on Nov 6 in Los Angeles.
  const now = new Date("2026-11-06T17:00:00Z");

  it("calls a deadline later today today, in the label and the pill alike", () => {
    expect(describeDue("2026-11-07T05:00:00Z", zone, now)).toMatchObject({
      relative: "today",
      pill: { label: "Due today" },
    });
  });

  it("calls a deadline 20 hours away tomorrow and soon, not today", () => {
    expect(describeDue("2026-11-07T13:00:00Z", zone, now)).toMatchObject({
      relative: "tomorrow",
      pill: { label: "Due soon" },
    });
  });

  it("marks a passed deadline overdue", () => {
    expect(describeDue("2026-11-05T20:00:00Z", zone, now)).toMatchObject({
      relative: "yesterday",
      pill: { label: "Overdue" },
    });
  });

  it("drops the pill a week out and both labels for no date", () => {
    expect(describeDue("2026-11-13T17:00:00Z", zone, now)).toEqual({
      relative: "in 7d",
      pill: null,
    });
    expect(describeDue(null, zone, now)).toEqual({ relative: null, pill: null });
  });

  it("counts days in the profile's time zone, not the browser's", () => {
    // 22:00 on Nov 6 in Los Angeles is already Nov 7 in New York.
    expect(describeDue("2026-11-07T06:00:00Z", zone, now).relative).toBe("today");
    expect(describeDue("2026-11-07T06:00:00Z", "America/New_York", now).relative).toBe("tomorrow");
  });
});

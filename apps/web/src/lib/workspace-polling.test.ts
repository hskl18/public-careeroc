import { describe, expect, it } from "vitest";
import type { DashboardResponse, InboxOverview } from "./dashboard";
import { isInboxAnalysisActive } from "./workspace-polling";

function dashboardWith(inbox: Partial<InboxOverview>) {
  return {
    inbox: {
      connectedAddress: "alex@demo.careeroc.example",
      syncedEmails: 47,
      processedEmails: 47,
      lastSyncAtUtc: "2026-11-04T20:31:00Z",
      ...inbox,
    },
  } as DashboardResponse;
}

describe("isInboxAnalysisActive", () => {
  it("is idle once every synced email is analyzed", () => {
    expect(isInboxAnalysisActive(dashboardWith({}))).toBe(false);
  });

  it.each([
    ["before the first sync", { lastSyncAtUtc: null }],
    ["while jobs are pending", { pendingAnalysisEmails: 3 }],
    ["while jobs are running", { processingAnalysisEmails: 1 }],
    ["while a backfill runs", { backfillStatus: "Running" }],
    ["while fewer emails are analyzed than synced", { processedEmails: 40, scannedCount: 40 }],
    ["while the estimate is ahead of the scan", { totalEstimate: 120, scannedCount: 47 }],
  ] as const)("is active %s", (_, inbox) => {
    expect(isInboxAnalysisActive(dashboardWith(inbox))).toBe(true);
  });
});

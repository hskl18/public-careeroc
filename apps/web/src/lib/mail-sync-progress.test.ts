import { describe, expect, it } from "vitest";
import type { DashboardResponse, InboxOverview } from "./dashboard";
import {
  buildMailSyncStatus,
  buildProgressEstimate,
  updateProgressSamples,
  type ProgressSample,
} from "./mail-sync-progress";

function dashboardWith(inbox: Partial<InboxOverview>) {
  return {
    inbox: {
      connectedAddress: "alex@demo.careeroc.example",
      syncedEmails: 100,
      processedEmails: 40,
      lastSyncAtUtc: "2026-11-05T12:00:00Z",
      ...inbox,
    },
  } as DashboardResponse;
}

const baseStatus = {
  setupLive: true,
  dashboardLive: true,
  connectedCount: 1,
  reconnectableCount: 0,
  setupNeededCount: 0,
  lastSyncAtUtc: "2026-11-05T12:00:00Z",
  analyzing: false,
  estimate: null,
};

describe("updateProgressSamples", () => {
  it("starts a new window when the processed count goes backwards", () => {
    const current: ProgressSample[] = [{ processed: 50, total: 100, capturedAtMs: 0 }];

    expect(updateProgressSamples(current, dashboardWith({ processedEmails: 40 }), 5000)).toEqual([
      { processed: 40, total: 100, capturedAtMs: 5000 },
    ]);
  });

  it("clears the window once everything is analyzed", () => {
    expect(updateProgressSamples([], dashboardWith({ processedEmails: 100 }), 0)).toEqual([]);
  });
});

describe("buildProgressEstimate", () => {
  const dashboard = dashboardWith({});

  it("reports a rate while analysis moves", () => {
    const samples = [
      { processed: 20, total: 100, capturedAtMs: 0 },
      { processed: 40, total: 100, capturedAtMs: 20_000 },
    ];

    expect(buildProgressEstimate(samples, 40, 100, dashboard)).toMatchObject({
      kind: "moving",
      label: "~1 min remaining · 1.0 emails/s",
    });
  });

  it("calls it stalled after two minutes without progress", () => {
    const samples = [
      { processed: 40, total: 100, capturedAtMs: 0 },
      { processed: 40, total: 100, capturedAtMs: 150_000 },
    ];

    expect(buildProgressEstimate(samples, 40, 100, dashboard)).toMatchObject({
      kind: "stalled",
      label: "Stalled at 40/100",
    });
  });

  it("names the failed jobs while it is still estimating", () => {
    const failing = dashboardWith({ failedAnalysisEmails: 2, lastAnalysisError: "timeout" });

    expect(buildProgressEstimate([], 40, 100, failing)?.detail).toBe(
      "60 emails remain. 2 analysis jobs failed. Latest error: timeout",
    );
  });
});

describe("buildMailSyncStatus", () => {
  it("puts a reconnect ahead of everything but an offline setup check", () => {
    expect(buildMailSyncStatus({ ...baseStatus, reconnectableCount: 1, dashboardLive: false }).label).toBe(
      "Reconnect Gmail",
    );
    expect(buildMailSyncStatus({ ...baseStatus, setupLive: false, reconnectableCount: 1 }).label).toBe(
      "API offline",
    );
  });

  it("shows a stalled analysis without the live pulse", () => {
    const status = buildMailSyncStatus({
      ...baseStatus,
      analyzing: true,
      estimate: { kind: "stalled", label: "Stalled at 40/100", detail: "Analysis is stuck." },
    });

    expect(status).toMatchObject({ label: "Analysis stalled", pulse: false, detail: "Analysis is stuck." });
  });

  it("waits for the first sync before calling an inbox synced", () => {
    expect(buildMailSyncStatus({ ...baseStatus, lastSyncAtUtc: null }).label).toBe("Waiting for first sync");
    expect(buildMailSyncStatus(baseStatus).label).toBe("Synced");
  });
});

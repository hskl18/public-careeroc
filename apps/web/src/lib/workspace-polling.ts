import type { DashboardResponse } from "./dashboard";

export const activeWorkspacePollingIntervalMs = 45_000;
export const activeMailSyncPollingIntervalMs = 15_000;
export const idleWorkspacePollingIntervalMs = 300_000;
export const setupPollingIntervalMs = 600_000;

// One answer to "is the inbox still syncing or being analyzed?", so the header pill, the dashboard,
// and the inbox page poll and report the same state.
export function isInboxAnalysisActive(dashboard: DashboardResponse) {
  const inbox = dashboard.inbox;
  const scanned = Math.max(0, inbox.scannedCount ?? inbox.processedEmails);
  const total = Math.max(inbox.totalEstimate ?? 0, inbox.syncedEmails, inbox.processedEmails, scanned, 0);
  return (
    !inbox.lastSyncAtUtc ||
    (inbox.pendingAnalysisEmails ?? 0) > 0 ||
    (inbox.processingAnalysisEmails ?? 0) > 0 ||
    inbox.backfillStatus?.toLowerCase() === "running" ||
    (total > 0 && Math.min(scanned, total) < total)
  );
}

import type { DashboardResponse } from "./dashboard";
import {
  activeMailSyncPollingIntervalMs,
  idleWorkspacePollingIntervalMs,
  isInboxAnalysisActive,
} from "./workspace-polling";

export type ProgressSample = {
  processed: number;
  total: number;
  capturedAtMs: number;
};

export type ProgressEstimate = {
  kind: "estimating" | "moving" | "stalled";
  label: string;
  detail: string;
};

export type MailSyncStatus = {
  label: string;
  tone: string;
  dot: string;
  pulse: boolean;
  detail: string;
};

const stalledWindowMs = 120000;
const progressWindowSize = Math.ceil(stalledWindowMs / activeMailSyncPollingIntervalMs) + 2;
const minimumMovingRate = 0.01;

export function formatCount(value: number | null | undefined) {
  return Math.max(0, value ?? 0);
}

function plural(count: number, singular: string, pluralForm = `${singular}s`) {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

export function getDashboardProgress(dashboard: DashboardResponse) {
  const syncedEmails = Math.max(0, dashboard.inbox.syncedEmails);
  const processedEmails = Math.max(0, dashboard.inbox.processedEmails);
  const total = Math.max(syncedEmails, processedEmails);
  const processed = total === 0 ? 0 : Math.min(processedEmails, total);

  return {
    processed,
    total,
    percent: total === 0 ? 0 : Math.round((processed / total) * 100),
  };
}

export function getPollingIntervalMs(dashboard: DashboardResponse, connectedAccountCount: number) {
  if (connectedAccountCount === 0) {
    return null;
  }

  return isInboxAnalysisActive(dashboard)
    ? activeMailSyncPollingIntervalMs
    : idleWorkspacePollingIntervalMs;
}

// The active poll also asks the API to advance processing; the idle poll only reads.
export function shouldAdvanceInboxProcessing(dashboard: DashboardResponse, connectedAccountCount: number) {
  return getPollingIntervalMs(dashboard, connectedAccountCount) === activeMailSyncPollingIntervalMs;
}

// Keeps a sliding window of progress readings; a reset or a shrinking count starts a new window.
export function updateProgressSamples(
  current: ProgressSample[],
  dashboard: DashboardResponse,
  capturedAtMs: number,
) {
  const { processed, total } = getDashboardProgress(dashboard);

  if (total === 0 || processed >= total) {
    return [];
  }

  const nextSample = { processed, total, capturedAtMs };
  const previous = current.at(-1);

  if (
    !previous ||
    processed < previous.processed ||
    total < previous.total ||
    capturedAtMs - previous.capturedAtMs < 1000
  ) {
    return [nextSample];
  }

  return [...current, nextSample].slice(-progressWindowSize);
}

function formatRemaining(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return "~1 min remaining";
  }

  if (seconds < 60) {
    return `~${Math.max(5, Math.round(seconds / 5) * 5)} sec remaining`;
  }

  return `~${Math.max(1, Math.round(seconds / 60))} min remaining`;
}

function formatRate(rate: number) {
  return `${rate < 0.1 ? rate.toFixed(2) : rate.toFixed(1)} emails/s`;
}

function formatElapsed(seconds: number) {
  if (seconds < 60) {
    return `${Math.max(5, Math.round(seconds / 5) * 5)} sec`;
  }

  return `${Math.max(1, Math.round(seconds / 60))} min`;
}

function buildAnalysisBacklogDetail(dashboard: DashboardResponse) {
  const pending = formatCount(dashboard.inbox.pendingAnalysisEmails);
  const processing = formatCount(dashboard.inbox.processingAnalysisEmails);
  const failed = formatCount(dashboard.inbox.failedAnalysisEmails);
  const lastError = dashboard.inbox.lastAnalysisError?.trim();

  if (failed > 0) {
    return `${plural(failed, "analysis job")} failed.${lastError ? ` Latest error: ${lastError}` : ""}`;
  }

  if (processing > 0) {
    return `${processing} email${processing === 1 ? " is" : "s are"} locked in analysis; the worker may be stuck if this count does not change.`;
  }

  if (pending > 0) {
    return `${pending} email${pending === 1 ? " is" : "s are"} waiting for analysis; the worker may be idle or blocked.`;
  }

  return "No pending analysis job is visible, so the dashboard may be waiting on stale processing state.";
}

export function buildProgressEstimate(
  samples: ProgressSample[],
  processed: number,
  total: number,
  dashboard: DashboardResponse,
): ProgressEstimate | null {
  if (total === 0 || processed >= total) {
    return null;
  }

  const latest = samples.at(-1);
  const oldest = samples[0];
  const remaining = Math.max(0, total - processed);

  if (!latest || !oldest || samples.length < 2) {
    return {
      kind: "estimating",
      label: "Checking analysis worker...",
      detail: `${plural(remaining, "email")} remain. ${buildAnalysisBacklogDetail(dashboard)}`,
    };
  }

  const elapsedSeconds = (latest.capturedAtMs - oldest.capturedAtMs) / 1000;
  const processedDelta = latest.processed - oldest.processed;
  const rate = elapsedSeconds > 0 ? processedDelta / elapsedSeconds : 0;

  if (rate >= minimumMovingRate) {
    return {
      kind: "moving",
      label: `${formatRemaining(remaining / rate)} · ${formatRate(rate)}`,
      detail: `Analysis is moving. ${plural(remaining, "email")} remain at ${formatRate(rate)}.`,
    };
  }

  if (latest.capturedAtMs - oldest.capturedAtMs < stalledWindowMs) {
    const processing = formatCount(dashboard.inbox.processingAnalysisEmails);

    return {
      kind: "estimating",
      label: processing > 0 ? "Model analysis running..." : "Checking for progress...",
      detail: processing > 0
        ? `${plural(remaining, "email")} remain. ${processing} email${processing === 1 ? " is" : "s are"} currently in analysis; model calls can take up to the configured timeout before falling back.`
        : `${plural(remaining, "email")} remain. No completed analysis has appeared in the latest samples yet.`,
    };
  }

  return {
    kind: "stalled",
    label: `Stalled at ${processed}/${total}`,
    detail: `Analysis is stuck at ${processed}/${total}. No email finished processing for about ${formatElapsed(elapsedSeconds)}. ${buildAnalysisBacklogDetail(dashboard)} It retries automatically.`,
  };
}

const yellow = {
  tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
  dot: "bg-[var(--yellow)]",
};
const red = {
  tone: "border-[var(--red)]/25 bg-[var(--red-soft)] text-[var(--red)]",
  dot: "bg-[var(--red)]",
};
const blue = {
  tone: "border-[var(--blue)]/25 bg-[var(--blue-soft)] text-[var(--blue)]",
  dot: "bg-[var(--blue)]",
};
const green = {
  tone: "border-[var(--green)]/25 bg-[var(--green-soft)] text-[var(--green)]",
  dot: "bg-[var(--green)]",
};

// The badge on the Gmail sync card; the first problem that applies wins.
export function buildMailSyncStatus(input: {
  setupLive: boolean;
  dashboardLive: boolean;
  connectedCount: number;
  reconnectableCount: number;
  setupNeededCount: number;
  lastSyncAtUtc: string | null;
  analyzing: boolean;
  estimate: ProgressEstimate | null;
}): MailSyncStatus {
  if (!input.setupLive) {
    return { label: "API offline", ...yellow, pulse: false, detail: "Gmail setup status is unavailable." };
  }

  if (input.connectedCount === 0 && input.reconnectableCount === 0) {
    return { label: "Setup incomplete", ...yellow, pulse: false, detail: "Connect Gmail to start the first import." };
  }

  if (input.reconnectableCount > 0) {
    return { label: "Reconnect Gmail", ...red, pulse: false, detail: "Reconnect Google to resume sync." };
  }

  if (!input.dashboardLive) {
    return { label: "API offline", ...yellow, pulse: false, detail: "Live sync status is unavailable." };
  }

  if (input.setupNeededCount > 0) {
    return {
      label: "Finish setup",
      ...red,
      pulse: false,
      detail: `${input.setupNeededCount === 1 ? "1 Gmail connection needs" : `${input.setupNeededCount} Gmail connections need`} setup.`,
    };
  }

  if (!input.lastSyncAtUtc) {
    return { label: "Waiting for first sync", ...blue, pulse: true, detail: "Waiting for first sync." };
  }

  if (!input.analyzing) {
    return { label: "Synced", ...green, pulse: false, detail: "Up to date." };
  }

  const detail = input.estimate?.detail ?? "Analysis is running.";
  return input.estimate?.kind === "stalled"
    ? { label: "Analysis stalled", ...yellow, pulse: false, detail }
    : { label: "Analyzing mail", ...blue, pulse: true, detail };
}

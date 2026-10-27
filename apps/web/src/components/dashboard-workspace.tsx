"use client";

import { useEffect, useEffectEvent, useRef, useState } from "react";
import dynamic from "next/dynamic";
import { OnboardingHero, type OnboardingState } from "@/components/onboarding-hero";
import { WelcomeToast } from "@/components/welcome-toast";
import type { DashboardResponse, PendingAction } from "@/lib/dashboard";
import type {
  InboxAccountSetup,
  InboxSetupResponse,
} from "@/lib/inbox-accounts";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import {
  activeWorkspacePollingIntervalMs,
  idleWorkspacePollingIntervalMs,
} from "@/lib/workspace-polling";
import {
  ApplicationsOverviewSection,
  AttentionSection,
  SuccessToast,
  getSnoozePresetLabel,
  getSnoozeUntilUtc,
  type SnoozePreset,
} from "./dashboard-workspace-ui";

const RecentChangesSection = dynamic(() =>
  import("@/components/recent-changes-section").then(
    (module) => module.RecentChangesSection,
  ),
);

const processingOnboardingPollingIntervalMs = 5_000;

type DashboardWorkspaceProps = {
  initialDashboard: DashboardResponse;
  dashboardSource: "api" | "fallback";
  initialInboxSetup: InboxSetupResponse;
  inboxSetupSource: "api" | "fallback";
  proxyPrefix: string | null;
  currentUserEmail: string;
  currentUserName?: string | null;
  userTimeZoneId: string;
  welcomeDismissedAtUtc?: string | null;
};

type DashboardInboxCheckResponse = {
  cacheHit: boolean;
  dashboard: DashboardResponse;
};

type SyncNowResponse = {
  queued?: boolean;
  attemptedAtUtc?: string;
  retryAfterSeconds?: number;
  error?: string;
};

function formatTimestamp(value: string | null, timeZoneId: string) {
  if (!value) return "Waiting for first sync";

  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: timeZoneId,
  }).format(new Date(value));
}

function isConnectedAccount(account: InboxAccountSetup) {
  return account.connectionStatus === "Connected";
}

function computeOnboardingState(
  dashboard: DashboardResponse,
  setup: InboxSetupResponse,
): OnboardingState | null {
  const connected = setup.accounts.filter(isConnectedAccount);

  if (connected.length === 0) {
    return { kind: "connect-gmail" };
  }

  const emailAddress = connected[0]?.emailAddress ?? null;

  const backfillStatus = dashboard.inbox.backfillStatus?.toLowerCase() ?? null;
  const scannedCount = Math.max(
    0,
    dashboard.inbox.scannedCount ?? dashboard.inbox.processedEmails,
  );
  const syncedEmails = Math.max(0, dashboard.inbox.syncedEmails);
  const processedEmails = Math.max(0, dashboard.inbox.processedEmails);
  const total = Math.max(
    dashboard.inbox.totalEstimate ?? 0,
    syncedEmails,
    processedEmails,
    scannedCount,
  );
  const processed = total === 0 ? scannedCount : Math.min(scannedCount, total);
  const recognizedApplications =
    dashboard.inbox.recognizedApplications ?? dashboard.metrics.totalApplications;
  const latestActivity = dashboard.recentActivity[0] ?? null;

  if (!dashboard.inbox.lastSyncAtUtc && backfillStatus !== "running") {
    return { kind: "waiting-first-sync", emailAddress };
  }

  if (
    backfillStatus === "running" ||
    (total > 0 && processed < total && recognizedApplications === 0)
  ) {
    return {
      kind: "processing",
      emailAddress,
      processed,
      total,
      recognizedApplications,
      lastEventLabel: latestActivity
        ? `${latestActivity.company} — ${latestActivity.role}`
        : null,
    };
  }

  return null;
}

function shouldRunInboxCheck(setup: InboxSetupResponse) {
  return setup.accounts.some(isConnectedAccount);
}

function getWorkspacePollingIntervalMs(
  dashboard: DashboardResponse,
  setup: InboxSetupResponse,
) {
  if (!shouldRunInboxCheck(setup)) {
    return null;
  }

  const scannedCount = Math.max(
    0,
    dashboard.inbox.scannedCount ?? dashboard.inbox.processedEmails,
  );
  const syncedEmails = Math.max(0, dashboard.inbox.syncedEmails);
  const processedEmails = Math.max(0, dashboard.inbox.processedEmails);
  const total = Math.max(
    dashboard.inbox.totalEstimate ?? 0,
    syncedEmails,
    processedEmails,
    scannedCount,
  );
  const processed = total === 0 ? scannedCount : Math.min(scannedCount, total);
  const hasActiveAnalysis =
    (dashboard.inbox.pendingAnalysisEmails ?? 0) > 0 ||
    (dashboard.inbox.processingAnalysisEmails ?? 0) > 0 ||
    dashboard.inbox.backfillStatus?.toLowerCase() === "running" ||
    (total > 0 && processed < total);

  return hasActiveAnalysis || !dashboard.inbox.lastSyncAtUtc
    ? activeWorkspacePollingIntervalMs
    : idleWorkspacePollingIntervalMs;
}

function shouldAdvanceInboxProcessing(
  dashboard: DashboardResponse,
  setup: InboxSetupResponse,
) {
  return getWorkspacePollingIntervalMs(dashboard, setup) === activeWorkspacePollingIntervalMs;
}

function getFirstName(fullName: string | null | undefined) {
  if (!fullName) return null;
  const trimmed = fullName.trim();
  if (!trimmed) return null;
  return trimmed.split(/\s+/)[0] ?? null;
}

function removePendingAction(
  dashboard: DashboardResponse,
  action: PendingAction,
): DashboardResponse {
  const nextPendingActions = dashboard.pendingActions.filter(
    (candidate) => candidate.id !== action.id,
  );
  const stillNeedsAttention = nextPendingActions.some(
    (candidate) => candidate.jobApplicationId === action.jobApplicationId,
  );

  return {
    ...dashboard,
    metrics: {
      ...dashboard.metrics,
      needsAction: stillNeedsAttention
        ? dashboard.metrics.needsAction
        : Math.max(0, dashboard.metrics.needsAction - 1),
    },
    pendingActions: nextPendingActions,
    applications: dashboard.applications.map((application) =>
      application.id === action.jobApplicationId
        ? { ...application, actionRequired: stillNeedsAttention }
        : application,
    ),
  };
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as SyncNowResponse;
    if (payload.retryAfterSeconds) {
      return `Sync is rate limited. Try again in ${payload.retryAfterSeconds}s.`;
    }
    if (payload.error === "sync_now_rate_limited") {
      return "Sync is rate limited. Try again in 30s.";
    }
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

export function DashboardWorkspace({
  initialDashboard,
  dashboardSource,
  initialInboxSetup,
  inboxSetupSource,
  proxyPrefix,
  currentUserEmail,
  currentUserName,
  userTimeZoneId,
  welcomeDismissedAtUtc,
}: DashboardWorkspaceProps) {
  const formatDateTime = (value: string | null) =>
    formatTimestamp(value, userTimeZoneId);
  const [dashboard, setDashboard] = useState(initialDashboard);
  const [inboxSetup, setInboxSetup] = useState(initialInboxSetup);
  const [pendingReminderKey, setPendingReminderKey] = useState<string | null>(
    null,
  );
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [syncNowPending, setSyncNowPending] = useState(false);
  const [lastSyncNowClickAtMs, setLastSyncNowClickAtMs] = useState<number | null>(null);
  const initialInboxCheckStarted = useRef(false);
  const workspaceStateRef = useRef({ dashboard, inboxSetup });

  const canPersistReminders = Boolean(proxyPrefix) && dashboardSource === "api";
  const liveData = dashboardSource === "api" && inboxSetupSource === "api";
  const attentionAccounts = inboxSetup.accounts.filter(
    (account) => !isConnectedAccount(account),
  );
  const onboardingState = liveData
    ? computeOnboardingState(dashboard, inboxSetup)
    : null;
  const coldStart = onboardingState !== null;
  const connectedInboxCount = inboxSetup.accounts.filter(isConnectedAccount).length;

  useEffect(() => {
    workspaceStateRef.current = { dashboard, inboxSetup };
  }, [dashboard, inboxSetup]);

  async function refreshDashboardFromApi({ bypassCache = false } = {}) {
    if (!proxyPrefix) return false;

    const cacheSuffix = bypassCache ? `?fresh=${Date.now()}` : "";
    const response = await fetchWithInFlightDedup(
      `${proxyPrefix}/api/dashboard${cacheSuffix}`,
      {
        cache: "no-store",
      },
    );

    if (!response.ok) return false;

    setDashboard((await response.json()) as DashboardResponse);
    return true;
  }

  async function refreshInboxSetupFromApi() {
    if (!proxyPrefix) return false;

    const response = await fetchWithInFlightDedup(`${proxyPrefix}/api/inbox-accounts`, {
      cache: "no-store",
    });

    if (!response.ok) return false;

    setInboxSetup((await response.json()) as InboxSetupResponse);
    return true;
  }

  async function refreshWorkspaceFromApi() {
    const [dashboardRefreshed, inboxRefreshed] = await Promise.all([
      refreshDashboardFromApi({ bypassCache: true }),
      refreshInboxSetupFromApi(),
    ]);

    return dashboardRefreshed || inboxRefreshed;
  }

  async function checkInboxFromApi() {
    if (!proxyPrefix) return false;

    const endpoint = `${proxyPrefix}/api/dashboard/inbox-check`;
    const response = await fetchWithInFlightDedup(
      endpoint,
      {
        method: "POST",
        cache: "no-store",
      },
      { dedupeKey: `POST:${endpoint}` },
    );

    if (!response.ok) return false;

    const payload = (await response.json()) as DashboardInboxCheckResponse;
    setDashboard(payload.dashboard);

    if (!payload.cacheHit) {
      await refreshInboxSetupFromApi();
    }

    return true;
  }

  const pollWorkspaceProgress = useEffectEvent(async () => {
    if (document.visibilityState === "hidden") {
      return;
    }

    const current = workspaceStateRef.current;
    if (shouldAdvanceInboxProcessing(current.dashboard, current.inboxSetup)) {
      await checkInboxFromApi();
      return;
    }

    await refreshWorkspaceFromApi();
  });

  const checkInboxOnLoad = useEffectEvent(async () => {
    const current = workspaceStateRef.current;
    if (!shouldAdvanceInboxProcessing(current.dashboard, current.inboxSetup)) {
      return;
    }

    await checkInboxFromApi();
  });

  const pollProcessingOnboarding = useEffectEvent(async () => {
    await refreshDashboardFromApi({ bypassCache: true });
  });

  useEffect(() => {
    if (!liveData || initialInboxCheckStarted.current) {
      return;
    }

    initialInboxCheckStarted.current = true;
    void checkInboxOnLoad().catch(() => {});
  }, [liveData]);

  useEffect(() => {
    if (!liveData) {
      return;
    }

    let cancelled = false;
    let timeoutId: number | null = null;

    function scheduleNextPoll() {
      const current = workspaceStateRef.current;
      const pollingIntervalMs = getWorkspacePollingIntervalMs(
        current.dashboard,
        current.inboxSetup,
      );

      if (cancelled || pollingIntervalMs === null) {
        return;
      }

      timeoutId = window.setTimeout(() => {
        void pollWorkspaceProgress()
          .catch(() => {})
          .finally(() => {
            if (!cancelled) {
              scheduleNextPoll();
            }
          });
      }, pollingIntervalMs);
    }

    scheduleNextPoll();

    return () => {
      cancelled = true;
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
      }
    };
  }, [liveData]);

  useEffect(() => {
    if (!liveData || onboardingState?.kind !== "processing") {
      return;
    }

    const interval = window.setInterval(() => {
      if (document.visibilityState === "hidden") {
        return;
      }

      void pollProcessingOnboarding().catch(() => {});
    }, processingOnboardingPollingIntervalMs);

    return () => {
      window.clearInterval(interval);
    };
  }, [liveData, onboardingState?.kind]);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timeout = window.setTimeout(() => {
      setNotice(null);
    }, 7000);

    return () => {
      window.clearTimeout(timeout);
    };
  }, [notice]);

  async function handleReminderAction(
    action: PendingAction,
    status: "Completed" | "Dismissed",
  ) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const requestKey = `${action.id}:${status}`;
    setPendingReminderKey(requestKey);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/reminders/${action.id}/status`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ status }),
        },
      );

      if (!response.ok) {
        throw new Error(`Request failed with ${response.status}`);
      }

      const refreshed = await refreshDashboardFromApi({ bypassCache: true });

      if (!refreshed) {
        setDashboard((current) => removePendingAction(current, action));
      }

      const verb = status === "Completed" ? "Marked done" : "Dismissed";
      setNotice(`${verb}: "${action.title}".`);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to update reminder.",
      );
    } finally {
      setPendingReminderKey(null);
    }
  }

  async function handleReminderSnooze(
    action: PendingAction,
    preset: SnoozePreset,
  ) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const requestKey = `${action.id}:snooze:${preset}`;
    setPendingReminderKey(requestKey);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/reminders/${action.id}/snooze`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ until: getSnoozeUntilUtc(preset) }),
        },
      );

      if (!response.ok) {
        throw new Error(`Request failed with ${response.status}`);
      }

      const refreshed = await refreshDashboardFromApi({ bypassCache: true });

      if (!refreshed) {
        setDashboard((current) => removePendingAction(current, action));
      }

      setNotice(`Snoozed until ${getSnoozePresetLabel(preset).toLowerCase()}: "${action.title}".`);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to snooze reminder.",
      );
    } finally {
      setPendingReminderKey(null);
    }
  }

  async function handleSyncNow() {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const now = Date.now();
    if (lastSyncNowClickAtMs && now - lastSyncNowClickAtMs < 30_000) {
      const waitSeconds = Math.ceil((30_000 - (now - lastSyncNowClickAtMs)) / 1000);
      setError(`Sync was just requested. Try again in ${waitSeconds}s.`);
      return;
    }

    setSyncNowPending(true);
    setLastSyncNowClickAtMs(now);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/inbox-accounts/sync-now`, {
        method: "POST",
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const payload = (await response.json()) as SyncNowResponse;
      setNotice(
        payload.queued
          ? "Inbox sync queued. Dashboard progress will refresh automatically."
          : "Sync request accepted.",
      );

      await refreshDashboardFromApi({ bypassCache: true });

      window.setTimeout(() => {
        void refreshDashboardFromApi({ bypassCache: true }).catch(() => {});
      }, 8_000);
    } catch (syncError) {
      setError(syncError instanceof Error ? syncError.message : "Sync request failed.");
    } finally {
      setSyncNowPending(false);
    }
  }

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell mx-auto flex w-full max-w-[104rem] flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-6">
        {!liveData ? (
          <div className="alert alert-warning shrink-0">
            Live API data is unavailable right now. This page is showing an
            empty local state until the API comes back.
          </div>
        ) : null}
        {error ? <div className="alert alert-error shrink-0">{error}</div> : null}

        {liveData && connectedInboxCount > 0 ? (
          <section className="card app-workspace-panel shrink-0 p-3 sm:p-4">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className="eyebrow">Inbox sync</p>
                <p className="mt-1 text-sm text-[var(--text-secondary)]">
                  Last synced {formatDateTime(dashboard.inbox.lastSyncAtUtc)}
                  {dashboard.inbox.backfillStatus
                    ? ` · backfill ${dashboard.inbox.backfillStatus}`
                    : ""}
                </p>
              </div>
              <button
                type="button"
                className="btn btn-secondary btn-sm w-full sm:w-auto"
                disabled={!proxyPrefix || syncNowPending}
                onClick={() => void handleSyncNow()}
              >
                {syncNowPending ? "Syncing..." : "Sync inbox now"}
              </button>
            </div>
          </section>
        ) : null}

        {onboardingState ? (
          <div className="shrink-0">
            <OnboardingHero
              state={onboardingState}
              recentActivity={dashboard.recentActivity}
            />
          </div>
        ) : null}

        <div className="shrink-0">
          <ApplicationsOverviewSection dashboard={dashboard} coldStart={coldStart} />
        </div>

        <section className="grid min-h-0 flex-1 items-stretch gap-4 [grid-template-rows:minmax(0,1fr)] xl:grid-cols-[minmax(0,11fr)_minmax(0,9fr)]">
          <div className="workspace-scroll-region min-h-0">
            <RecentChangesSection
              recentActivity={dashboard.recentActivity}
              coldStart={coldStart}
              timeZoneId={userTimeZoneId}
            />
          </div>
          <div className="workspace-scroll-region min-h-0">
            <AttentionSection
              liveData={liveData}
              canPersistReminders={canPersistReminders}
              pendingActions={dashboard.pendingActions}
              applications={dashboard.applications}
              attentionAccounts={attentionAccounts}
              pendingReminderKey={pendingReminderKey}
              coldStart={coldStart}
              formatDateTime={formatDateTime}
              onReminderAction={(action, status) =>
                void handleReminderAction(action, status)
              }
              onReminderSnooze={(action, preset) =>
                void handleReminderSnooze(action, preset)
              }
            />
          </div>
        </section>
      </div>
      {notice ? (
        <SuccessToast message={notice} onDismiss={() => setNotice(null)} />
      ) : null}
      {/* The welcome walks through connecting Gmail, so it is moot once an inbox (or the demo's) is connected. */}
      {connectedInboxCount === 0 ? (
        <WelcomeToast
          currentUserEmail={currentUserEmail}
          firstName={getFirstName(currentUserName)}
          initiallyDismissed={Boolean(welcomeDismissedAtUtc)}
        />
      ) : null}
    </main>
  );
}

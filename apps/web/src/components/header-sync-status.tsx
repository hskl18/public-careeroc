"use client";

import type { Route } from "next";
import Link from "next/link";
import { useEffect, useEffectEvent, useRef, useState } from "react";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import type { DashboardResponse } from "@/lib/dashboard";
import {
  activeWorkspacePollingIntervalMs,
  idleWorkspacePollingIntervalMs,
  setupPollingIntervalMs,
} from "@/lib/workspace-polling";
import type {
  InboxAccountSetup,
  InboxSetupResponse,
} from "@/lib/inbox-accounts";
import { useHydrated } from "@/lib/use-hydrated";

type HeaderSyncStatusProps = {
  initialDashboard: DashboardResponse;
  dashboardSource: "api" | "fallback";
  initialInboxSetup: InboxSetupResponse;
  inboxSetupSource: "api" | "fallback";
  timeZoneId: string;
};

type DashboardInboxCheckResponse = {
  dashboard: DashboardResponse;
};

type LiveSources = {
  dashboard: "api" | "fallback";
  inboxSetup: "api" | "fallback";
};

type HeaderSyncState = {
  dashboard: DashboardResponse;
  setup: InboxSetupResponse;
  liveSources: LiveSources;
};

const stableHydrationStatus = {
  label: "Sync status",
  meta: "",
  title: "Mail sync status is loading.",
  tone: "border-[var(--border)] bg-[var(--surface-elevated)] text-[var(--text-tertiary)]",
  dotColor: "bg-[var(--text-tertiary)]",
  pulse: false,
  href: "/app/inbox#mail-sync",
};

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "waiting");
}

function isConnectedAccount(account: InboxAccountSetup) {
  return account.connectionStatus === "Connected";
}

function getDashboardProgress(dashboard: DashboardResponse) {
  const syncedEmails = Math.max(0, dashboard.inbox.syncedEmails);
  const processedEmails = Math.max(0, dashboard.inbox.processedEmails);
  const total = Math.max(syncedEmails, processedEmails);
  const processed = total === 0 ? 0 : Math.min(processedEmails, total);

  return { processed, total };
}

function shouldRunInboxCheck(accounts: InboxAccountSetup[]) {
  return accounts.some(isConnectedAccount);
}

function shouldAdvanceInboxProcessing(
  accounts: InboxAccountSetup[],
  dashboard: DashboardResponse,
) {
  if (!shouldRunInboxCheck(accounts)) {
    return false;
  }

  const { processed, total } = getDashboardProgress(dashboard);
  return !dashboard.inbox.lastSyncAtUtc || (total > 0 && processed < total);
}

function getHeaderPollingIntervalMs(
  accounts: InboxAccountSetup[],
  dashboard: DashboardResponse,
) {
  const connectedAccounts = accounts.filter(isConnectedAccount);
  if (connectedAccounts.length === 0) {
    return setupPollingIntervalMs;
  }

  const { processed, total } = getDashboardProgress(dashboard);
  if (!dashboard.inbox.lastSyncAtUtc || (total > 0 && processed < total)) {
    return activeWorkspacePollingIntervalMs;
  }

  return idleWorkspacePollingIntervalMs;
}

function buildStatus(
  sources: LiveSources,
  accounts: InboxAccountSetup[],
  dashboard: DashboardResponse,
  timeZoneId: string,
) {
  const connectedAccounts = accounts.filter(isConnectedAccount);
  const attentionAccounts = accounts.filter((account) => !isConnectedAccount(account));
  const { processed, total } = getDashboardProgress(dashboard);
  const countLabel = total > 0 ? `${processed}/${total}` : "";

  if (sources.inboxSetup !== "api") {
    return {
      label: "API offline",
      meta: "",
      title: "Gmail setup status is unavailable.",
      tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
      dotColor: "bg-[var(--yellow)]",
      pulse: false,
      href: "/app/inbox#mail-sync",
    };
  }

  if (connectedAccounts.length === 0) {
    return {
      label: "Connect Gmail",
      meta: "1 min",
      title: "No Gmail inbox is connected. Click to open setup.",
      tone: "border-[var(--accent-ink)]/35 bg-[var(--accent-soft)] text-[var(--accent-ink)]",
      dotColor: "bg-[var(--accent)]",
      pulse: true,
      href: "/app/inbox#connect-inbox",
    };
  }

  if (sources.dashboard !== "api") {
    return {
      label: "API offline",
      meta: "",
      title: "Live sync status is unavailable.",
      tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
      dotColor: "bg-[var(--yellow)]",
      pulse: false,
      href: "/app/inbox#mail-sync",
    };
  }

  if (attentionAccounts.length > 0) {
    return {
      label: "Gmail needs attention",
      meta: "",
      title: `${attentionAccounts.length} inbox connection${attentionAccounts.length === 1 ? "" : "s"} need attention. Click to fix.`,
      tone: "border-[var(--red)]/25 bg-[var(--red-soft)] text-[var(--red)]",
      dotColor: "bg-[var(--red)]",
      pulse: true,
      href: "/app/inbox#connect-inbox",
    };
  }

  if (!dashboard.inbox.lastSyncAtUtc) {
    return {
      label: "Waiting",
      meta: "first sync",
      title: "Gmail is connected and waiting for the first sync. Click to open sync status.",
      tone: "border-[var(--blue)]/25 bg-[var(--blue-soft)] text-[var(--blue)]",
      dotColor: "bg-[var(--blue)]",
      pulse: true,
      href: "/app/inbox#mail-sync",
    };
  }

  if (total > 0 && processed < total) {
    return {
      label: "Analyzing inbox",
      meta: countLabel,
      title: `Last sync ${formatTimestamp(dashboard.inbox.lastSyncAtUtc, timeZoneId)}. ${processed} of ${total} emails analyzed.`,
      tone: "border-[var(--blue)]/25 bg-[var(--blue-soft)] text-[var(--blue)]",
      dotColor: "bg-[var(--blue)]",
      pulse: true,
      href: "/app/inbox#mail-sync",
    };
  }

  return {
    label: "Inbox synced",
    meta: total > 0 ? `${total} items` : "",
    title: `Last sync ${formatTimestamp(dashboard.inbox.lastSyncAtUtc, timeZoneId)}. ${total} Gmail item${total === 1 ? "" : "s"} imported. New Gmail messages sync automatically.`,
    tone: "border-[var(--green)]/25 bg-[var(--green-soft)] text-[var(--green)]",
    dotColor: "bg-[var(--green)]",
    pulse: false,
    href: "/app/inbox#mail-sync",
  };
}

export function HeaderSyncStatus({
  initialDashboard,
  dashboardSource,
  initialInboxSetup,
  inboxSetupSource,
  timeZoneId,
}: HeaderSyncStatusProps) {
  const [dashboard, setDashboard] = useState(initialDashboard);
  const [setup, setSetup] = useState(initialInboxSetup);
  const [liveSources, setLiveSources] = useState({
    dashboard: dashboardSource,
    inboxSetup: inboxSetupSource,
  });
  const retryAfterUntilMs = useRef(0);
  const syncStateRef = useRef<HeaderSyncState>({
    dashboard,
    setup,
    liveSources,
  });
  const hydrated = useHydrated();
  const status = hydrated
    ? buildStatus(liveSources, setup.accounts, dashboard, timeZoneId)
    : stableHydrationStatus;

  useEffect(() => {
    syncStateRef.current = { dashboard, setup, liveSources };
  }, [dashboard, setup, liveSources]);

  const refreshSyncStatus = useEffectEvent(async () => {
    if (document.visibilityState === "hidden") {
      return;
    }

    if (Date.now() < retryAfterUntilMs.current) {
      return;
    }

    let setupResponse: Response;
    try {
      setupResponse = await fetchWithInFlightDedup("/api/careeroc/api/inbox-accounts", {
        cache: "no-store",
      });
    } catch {
      setLiveSources((current) => ({ ...current, inboxSetup: "fallback" }));
      throw new Error("Header inbox setup request failed.");
    }

    if (!setupResponse.ok) {
      if (setupResponse.status === 429) {
        retryAfterUntilMs.current = resolveRetryAfterUntilMs(
          setupResponse.headers.get("retry-after"),
        );
      }

      setLiveSources((current) => ({ ...current, inboxSetup: "fallback" }));
      throw new Error("Header inbox setup request failed.");
    }

    const nextSetup = (await setupResponse.json()) as InboxSetupResponse;
    setSetup(nextSetup);
    setLiveSources((current) => ({ ...current, inboxSetup: "api" }));
    const advanceProcessing = shouldAdvanceInboxProcessing(
      nextSetup.accounts,
      syncStateRef.current.dashboard,
    );
    let dashboardResponse: Response;
    const dashboardEndpoint = advanceProcessing
      ? "/api/careeroc/api/dashboard/inbox-check"
      : "/api/careeroc/api/dashboard";
    try {
      dashboardResponse = await fetchWithInFlightDedup(
        dashboardEndpoint,
        {
          method: advanceProcessing ? "POST" : "GET",
          cache: "no-store",
        },
        advanceProcessing ? { dedupeKey: `POST:${dashboardEndpoint}` } : undefined,
      );
    } catch {
      setLiveSources((current) => ({ ...current, dashboard: "fallback" }));
      throw new Error("Header dashboard request failed.");
    }

    if (!dashboardResponse.ok) {
      setLiveSources((current) => ({ ...current, dashboard: "fallback" }));
      throw new Error("Header dashboard request failed.");
    }

    const nextDashboard = advanceProcessing
      ? ((await dashboardResponse.json()) as DashboardInboxCheckResponse).dashboard
      : ((await dashboardResponse.json()) as DashboardResponse);

    setDashboard(nextDashboard);
    setLiveSources({ dashboard: "api", inboxSetup: "api" });
  });

  useEffect(() => {
    let cancelled = false;
    let timeoutId: number | null = null;

    async function refresh() {
      try {
        await refreshSyncStatus();
        if (cancelled) return;
      } catch {
        if (cancelled) return;
      }
    }

    function scheduleNextPoll() {
      const current = syncStateRef.current;
      const pollingIntervalMs = getHeaderPollingIntervalMs(
        current.setup.accounts,
        current.dashboard,
      );

      timeoutId = window.setTimeout(() => {
        void refresh().finally(() => {
          if (!cancelled) {
            scheduleNextPoll();
          }
        });
      }, pollingIntervalMs);
    }

    const current = syncStateRef.current;
    if (
      current.liveSources.dashboard !== "api" ||
      current.liveSources.inboxSetup !== "api"
    ) {
      void refresh();
    }

    scheduleNextPoll();

    return () => {
      cancelled = true;
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
      }
    };
  }, []);

  return (
    <Link
      href={status.href as Route}
      title={status.title}
      className={`hidden h-8 max-w-[260px] items-center gap-2 rounded-md border px-2.5 text-[11px] font-medium transition-colors hover:bg-[var(--surface)] md:inline-flex ${status.tone}`}
    >
      <span
        className={`sync-dot ${status.dotColor} ${status.pulse ? "sync-dot-live" : ""}`}
      />
      <span className="truncate whitespace-nowrap">{status.label}</span>
      {status.meta ? (
        <span className="whitespace-nowrap font-mono text-[10px] text-[var(--text-tertiary)]">
          {status.meta}
        </span>
      ) : null}
    </Link>
  );
}

function resolveRetryAfterUntilMs(value: string | null) {
  if (!value) {
    return Date.now() + 60_000;
  }

  const seconds = Number(value);
  if (Number.isFinite(seconds) && seconds > 0) {
    return Date.now() + Math.min(seconds * 1000, 10 * 60_000);
  }

  const dateMs = Date.parse(value);
  return Number.isFinite(dateMs) && dateMs > Date.now()
    ? Math.min(dateMs, Date.now() + 10 * 60_000)
    : Date.now() + 60_000;
}

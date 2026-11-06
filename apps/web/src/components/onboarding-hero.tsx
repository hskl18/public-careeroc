"use client";

import Link from "next/link";
import type { RecentActivity } from "@/lib/dashboard";

export type OnboardingState =
  | { kind: "connect-gmail" }
  | { kind: "waiting-first-sync"; emailAddress: string | null }
  | {
      kind: "processing";
      emailAddress: string | null;
      processed: number;
      total: number;
      recognizedApplications: number;
      lastEventLabel: string | null;
    };

type OnboardingHeroProps = {
  state: OnboardingState;
  recentActivity: RecentActivity[];
};

type Step = {
  label: string;
  status: "done" | "current" | "upcoming";
};

function buildSteps(state: OnboardingState): Step[] {
  if (state.kind === "connect-gmail") {
    return [
      { label: "Sign in", status: "done" },
      { label: "Connect Gmail", status: "current" },
      { label: "First sync", status: "upcoming" },
    ];
  }

  if (state.kind === "waiting-first-sync") {
    return [
      { label: "Sign in", status: "done" },
      { label: "Connect Gmail", status: "done" },
      { label: "First sync", status: "current" },
    ];
  }

  return [
    { label: "Sign in", status: "done" },
    { label: "Connect Gmail", status: "done" },
    { label: "First sync", status: "current" },
  ];
}

function StepRail({ steps }: { steps: Step[] }) {
  return (
    <ol className="flex flex-wrap items-center gap-2">
      {steps.map((step, index) => {
        const isLast = index === steps.length - 1;
        const dotTone =
          step.status === "done"
            ? "bg-[var(--green)] text-[var(--on-accent)] border-[var(--green)]"
            : step.status === "current"
              ? "bg-[var(--accent)] text-[var(--on-accent)] border-[var(--accent-ink)]"
              : "bg-[var(--surface-elevated)] text-[var(--text-tertiary)] border-[var(--border)]";
        const labelTone =
          step.status === "upcoming"
            ? "text-[var(--text-tertiary)]"
            : "text-[var(--text-primary)]";

        return (
          <li key={step.label} className="flex items-center gap-2">
            <span
              className={`inline-flex h-6 w-6 items-center justify-center rounded-full border text-xs font-semibold ${dotTone}`}
              aria-hidden="true"
            >
              {step.status === "done" ? (
                <svg
                  viewBox="0 0 20 20"
                  className="h-3 w-3"
                  fill="none"
                  aria-hidden="true"
                >
                  <path
                    d="M5 10.4 8.2 13.5 15 6.5"
                    stroke="currentColor"
                    strokeWidth="2.2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
              ) : (
                index + 1
              )}
            </span>
            <span className={`text-xs font-medium ${labelTone}`}>
              {step.label}
            </span>
            {!isLast ? (
              <span
                className="h-px w-6 bg-[var(--border)]"
                aria-hidden="true"
              />
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}

function ProgressBar({
  processed,
  total,
  recognizedApplications,
  lastEventLabel,
  recentActivity,
}: {
  processed: number;
  total: number;
  recognizedApplications: number;
  lastEventLabel: string | null;
  recentActivity: RecentActivity[];
}) {
  const hasEstimate = total > 0;
  const safeTotal = hasEstimate ? total : 1;
  const percent = hasEstimate
    ? Math.max(4, Math.min(100, Math.round((processed / safeTotal) * 100)))
    : 35;
  const tickerItems = buildTickerItems(recentActivity, lastEventLabel).slice(0, 3);

  return (
    <div className="mt-5">
      <div className="flex items-center justify-between text-xs font-medium text-[var(--text-tertiary)]">
        <span>
          {hasEstimate
            ? `${processed} of ${total} emails analyzed`
            : `${processed} emails scanned`}
        </span>
        <span>{hasEstimate ? `${percent}%` : "scanning"}</span>
      </div>
      <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-[var(--surface-overlay)]">
        <div
          className="h-full rounded-full bg-[var(--accent)] transition-[width] duration-500 ease-out"
          style={{ width: `${percent}%` }}
        />
      </div>
      <div className="mt-3 grid gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-3 text-xs leading-5 text-[var(--text-secondary)] sm:grid-cols-[auto_minmax(0,1fr)] sm:items-start">
        <span className="badge w-fit bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]">
          Live backfill
        </span>
        <div className="min-w-0">
          <p className="font-medium text-[var(--text-primary)]">
            {recognizedApplications.toLocaleString("en-US")} application
            {recognizedApplications === 1 ? "" : "s"} recognized so far
          </p>
          {tickerItems.length > 0 ? (
            <ul className="mt-1 grid gap-1">
              {tickerItems.map((item) => (
                <li key={item} className="truncate">
                  Just imported: {item}
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function HeroCopy({ state }: { state: OnboardingState }) {
  if (state.kind === "connect-gmail") {
    return (
      <>
        <h2 className="text-base font-semibold text-[var(--text-primary)]">
          Connect Gmail to start
        </h2>
        <p className="mt-1 text-sm text-[var(--text-secondary)]">
          Reads recruiting mail only. About 2 minutes.
        </p>
      </>
    );
  }

  if (state.kind === "waiting-first-sync") {
    return (
      <>
        <h2 className="text-base font-semibold text-[var(--text-primary)]">
          First sync starting
        </h2>
        <p className="mt-1 text-sm text-[var(--text-secondary)]">
          {state.emailAddress ?? "Gmail"} is connected. This page fills in by itself.
        </p>
      </>
    );
  }

  return (
    <>
      <h2 className="text-base font-semibold text-[var(--text-primary)]">
        Reading your mail
      </h2>
      <p className="mt-1 text-sm text-[var(--text-secondary)]">
        Applications appear here as they are found. Usually a few minutes.
      </p>
    </>
  );
}

function HeroCta({ state }: { state: OnboardingState }) {
  if (state.kind === "connect-gmail") {
    return (
      <div className="flex flex-col items-start gap-2 sm:items-end">
        <Link
          href="/app/inbox#connect-inbox"
          className="btn btn-primary"
        >
          Connect Gmail
        </Link>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-start gap-2 sm:items-end">
      <Link href="/app/inbox#mail-sync" className="btn btn-secondary">
        Sync status
      </Link>
    </div>
  );
}

export function OnboardingHero({ state, recentActivity }: OnboardingHeroProps) {
  const steps = buildSteps(state);
  const isProcessing = state.kind === "processing";

  return (
    <section className="card relative overflow-hidden p-6">
      <div className="relative">
        <StepRail steps={steps} />

        <div className="mt-5 flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <HeroCopy state={state} />
          </div>
          <HeroCta state={state} />
        </div>

        {isProcessing ? (
          <ProgressBar
            processed={state.processed}
            total={state.total}
            recognizedApplications={state.recognizedApplications}
            lastEventLabel={state.lastEventLabel}
            recentActivity={recentActivity}
          />
        ) : null}
      </div>
    </section>
  );
}

function buildTickerItems(recentActivity: RecentActivity[], fallback: string | null) {
  const items = recentActivity.map((activity) =>
    `${activity.company} — ${activity.role}`,
  );

  if (fallback) {
    items.unshift(fallback);
  }

  return Array.from(new Set(items));
}

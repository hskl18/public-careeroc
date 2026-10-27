"use client";

import { useRouter } from "next/navigation";
import { type MouseEvent, useCallback, useEffect, useState } from "react";

type WelcomeToastProps = {
  currentUserEmail: string;
  firstName: string | null;
  initiallyDismissed: boolean;
};

const LEGACY_STORAGE_KEY = "oc:welcome-seen";

function getStorageKey(email: string) {
  return `oc:welcome-seen:${email.trim().toLowerCase() || "unknown"}`;
}

function getInitialDismissalState(
  currentUserEmail: string,
  initiallyDismissed: boolean,
) {
  if (initiallyDismissed || typeof window === "undefined") {
    return {
      dismissed: initiallyDismissed,
      needsServerPersistence: false,
    };
  }

  try {
    const storageKey = getStorageKey(currentUserEmail);
    const seen =
      window.localStorage.getItem(storageKey) ??
      window.localStorage.getItem(LEGACY_STORAGE_KEY);
    if (seen) {
      window.localStorage.setItem(storageKey, seen);
      return {
        dismissed: true,
        needsServerPersistence: true,
      };
    }
  } catch {
    // ignore storage access failures
  }

  return {
    dismissed: false,
    needsServerPersistence: false,
  };
}

export function WelcomeToast({
  currentUserEmail,
  firstName,
  initiallyDismissed,
}: WelcomeToastProps) {
  const router = useRouter();
  const [visible, setVisible] = useState(false);
  const [dismissalState, setDismissalState] = useState(() =>
    getInitialDismissalState(currentUserEmail, initiallyDismissed),
  );
  const dismissed = dismissalState.dismissed;

  const persistWelcomeDismissal = useCallback(async () => {
    try {
      window.localStorage.setItem(
        getStorageKey(currentUserEmail),
        new Date().toISOString(),
      );
    } catch {
      // ignore
    }

    try {
      await fetch("/api/careeroc/api/profile/welcome-dismissed", {
        method: "POST",
        credentials: "same-origin",
      });
      return true;
    } catch {
      // The local hide is still useful; the server will be retried on a later mount.
      return false;
    }
  }, [currentUserEmail]);

  useEffect(() => {
    if (!dismissalState.needsServerPersistence) {
      return;
    }

    let cancelled = false;
    void persistWelcomeDismissal().then((persisted) => {
      if (!cancelled && persisted) {
        setDismissalState({
          dismissed: true,
          needsServerPersistence: false,
        });
      }
    });

    return () => {
      cancelled = true;
    };
  }, [dismissalState.needsServerPersistence, persistWelcomeDismissal]);

  useEffect(() => {
    if (initiallyDismissed || dismissed) {
      return;
    }

    try {
      const timer = window.setTimeout(() => setVisible(true), 500);
      return () => window.clearTimeout(timer);
    } catch {
      // ignore storage access failures
    }
  }, [currentUserEmail, dismissed, initiallyDismissed]);

  async function dismiss() {
    setVisible(false);
    setDismissalState({
      dismissed: true,
      needsServerPersistence: false,
    });
    const persisted = await persistWelcomeDismissal();
    if (!persisted) {
      setDismissalState({
        dismissed: true,
        needsServerPersistence: true,
      });
    }
  }

  async function startSetup(event: MouseEvent<HTMLAnchorElement>) {
    event.preventDefault();
    await dismiss();
    router.push("/app/inbox#connect-inbox");
  }

  if (!visible || dismissed || initiallyDismissed) return null;

  const greetingName = firstName?.trim() || "there";

  return (
    <div className="fixed bottom-5 right-5 z-50 hidden w-[min(24rem,calc(100vw-2.5rem))] animate-in lg:block">
      <div className="relative overflow-hidden rounded-lg border border-[var(--border)] bg-[var(--surface-strong)] p-3 shadow-[0_18px_48px_rgba(17,33,45,0.18)] backdrop-blur-xl sm:p-4">
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 opacity-70"
          style={{
            background:
              "rgba(248,251,255,0.92)",
          }}
        />
        <div className="relative">
          <div className="flex items-start justify-between gap-3">
            <div>
              <p className="eyebrow">Welcome</p>
              <p className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
                Hi {greetingName} &mdash; let&rsquo;s get your inbox flowing
              </p>
            </div>
            <button
              type="button"
              aria-label="Dismiss welcome"
              className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[var(--text-tertiary)] transition-colors hover:bg-[var(--surface-overlay)] hover:text-[var(--text-primary)]"
              onClick={dismiss}
            >
              <span aria-hidden="true" className="text-base leading-none">
                x
              </span>
            </button>
          </div>

          <ol className="mt-3 hidden space-y-1.5 text-xs leading-6 text-[var(--text-secondary)] sm:block">
            <li>
              <span className="font-mono text-[var(--text-tertiary)]">1.</span>{" "}
              Connect Gmail in Inbox
            </li>
            <li>
              <span className="font-mono text-[var(--text-tertiary)]">2.</span>{" "}
              Pick how far back to backfill (defaults to 6 months)
            </li>
            <li>
              <span className="font-mono text-[var(--text-tertiary)]">3.</span>{" "}
              The Review Evidence Agent handles uncertain emails in the background
            </li>
          </ol>

          <div className="mt-3 flex items-center gap-2 sm:mt-4">
            <a
              href="/app/inbox#connect-inbox"
              className="btn btn-primary btn-sm"
              onClick={(event) => void startSetup(event)}
            >
              Start setup
            </a>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => void dismiss()}
            >
              Dismiss
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

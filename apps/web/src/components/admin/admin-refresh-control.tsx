"use client";

import { useCallback, useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";

export function AdminRefreshControl() {
  const router = useRouter();
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [isPending, startTransition] = useTransition();

  const refresh = useCallback(() => {
    startTransition(() => {
      router.refresh();
    });
  }, [router]);

  useEffect(() => {
    if (!autoRefresh) {
      return;
    }

    const interval = window.setInterval(refresh, 30_000);
    return () => window.clearInterval(interval);
  }, [autoRefresh, refresh]);

  return (
    <div className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        className="btn btn-secondary btn-sm"
        onClick={refresh}
        disabled={isPending}
      >
        {isPending ? "Refreshing" : "Refresh"}
      </button>
      <label className="inline-flex min-h-8 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 text-xs font-semibold text-[var(--text-secondary)]">
        <input
          type="checkbox"
          className="h-3.5 w-3.5 accent-[var(--accent)]"
          checked={autoRefresh}
          onChange={(event) => setAutoRefresh(event.currentTarget.checked)}
        />
        Auto 30s
      </label>
    </div>
  );
}

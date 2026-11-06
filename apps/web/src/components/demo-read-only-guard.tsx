"use client";

import { useEffect, useState } from "react";
import { demoReadOnlyMessage } from "@/lib/demo";

const noticeMs = 5000;
// Polled on load: it reads sync state, and the snapshot records its answer.
const inboxCheckPath = "/api/careeroc/api/dashboard/inbox-check";

// Rejecting here lets the catch in each of about 40 write handlers show the message, without threading a
// read-only flag through them, and it also covers future ones. The proxy's 403 is the real enforcement.
export function DemoReadOnlyGuard() {
  const [showNotice, setShowNotice] = useState(false);

  useEffect(() => {
    const originalFetch = window.fetch;
    let hideTimer: number | undefined;

    window.fetch = (input, init) => {
      const isRequest = input instanceof Request;
      const { pathname } = new URL(isRequest ? input.url : input, window.location.origin);
      const method = (init?.method ?? (isRequest ? input.method : "GET")).toUpperCase();
      if (method === "GET" || !pathname.startsWith("/api/careeroc/") || pathname === inboxCheckPath) {
        return originalFetch(input, init);
      }

      setShowNotice(true);
      window.clearTimeout(hideTimer);
      hideTimer = window.setTimeout(() => setShowNotice(false), noticeMs);
      return Promise.reject(new Error(demoReadOnlyMessage));
    };

    return () => {
      window.fetch = originalFetch;
      window.clearTimeout(hideTimer);
    };
  }, []);

  return (
    <div
      role="status"
      className="pointer-events-none fixed inset-x-0 bottom-20 z-[60] flex justify-center px-4 sm:bottom-5"
    >
      {showNotice ? (
        <p className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-4 py-2 text-sm font-medium text-[var(--text-primary)] shadow-lg">
          {demoReadOnlyMessage}
        </p>
      ) : null}
    </div>
  );
}

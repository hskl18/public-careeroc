"use client";

import Link from "next/link";
import { useEffect } from "react";

type RouteErrorStateProps = {
  error: Error & { digest?: string };
  reset: () => void;
  title?: string;
  description?: string;
};

export function RouteErrorState({
  error,
  reset,
  title = "Something went wrong.",
  description,
}: RouteErrorStateProps) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <main className="app-scroll-main">
      <div className="flex min-h-full w-full items-center justify-center px-3 py-6 sm:px-5">
        <section role="alert" className="card grid w-full max-w-md gap-3 p-5">
          <h1 className="text-base font-semibold text-[var(--text-primary)]">{title}</h1>
          {description ? (
            <p className="text-sm text-[var(--text-secondary)]">{description}</p>
          ) : null}
          {error.digest ? (
            <p className="text-xs text-[var(--text-tertiary)]">Error {error.digest}</p>
          ) : null}
          <div className="flex flex-wrap gap-2">
            <button type="button" className="btn btn-primary btn-sm" onClick={reset}>
              Try again
            </button>
            <Link href="/app" className="btn btn-secondary btn-sm">
              Dashboard
            </Link>
          </div>
        </section>
      </div>
    </main>
  );
}

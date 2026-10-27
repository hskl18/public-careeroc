"use client";

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
  description = "Retry the view. If it keeps failing, the API or session may need attention.",
}: RouteErrorStateProps) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-[60vh] w-full max-w-3xl items-center px-4 py-10">
        <section className="paper-card grid w-full gap-5 p-6 sm:p-8">
          <div className="grid gap-2">
            <p className="eyebrow">Recoverable error</p>
            <h1 className="text-2xl font-semibold">{title}</h1>
            <p className="text-sm leading-6 text-[var(--text-secondary)]">
              {description}
            </p>
            {error.digest ? (
              <p className="font-mono text-xs text-[var(--text-tertiary)]">
                Error digest: {error.digest}
              </p>
            ) : null}
          </div>
          <button type="button" className="btn btn-primary w-fit" onClick={reset}>
            Try again
          </button>
        </section>
      </div>
    </main>
  );
}

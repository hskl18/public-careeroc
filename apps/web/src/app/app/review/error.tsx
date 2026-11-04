"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function ReviewErrorBoundary({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <RouteErrorState
      error={error}
      reset={reset}
      title="Review queue could not load."
      description="Retry the queue. Review-blocked evidence remains unchanged while this page is unavailable."
    />
  );
}

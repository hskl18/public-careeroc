"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function ApplicationsErrorBoundary({
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
      title="Applications could not load."
      description="Retry the applications view. Existing application data is unchanged."
    />
  );
}

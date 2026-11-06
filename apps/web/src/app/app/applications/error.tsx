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
      description="Your data is unchanged."
    />
  );
}

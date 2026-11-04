"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function ResumeErrorBoundary({
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
      title="Resume intelligence could not load."
      description="Retry the resume view. Imports, corrections, and analysis can resume after the route recovers."
    />
  );
}

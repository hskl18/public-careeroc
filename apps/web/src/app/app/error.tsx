"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function WorkspaceErrorBoundary({
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
      title="The workspace could not load."
    />
  );
}

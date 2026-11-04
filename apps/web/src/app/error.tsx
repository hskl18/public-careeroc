"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function ErrorBoundary({
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
      title="The page could not load."
      description="Retry the page. If the problem continues, the public shell may be down or a required service returned an unexpected error."
    />
  );
}

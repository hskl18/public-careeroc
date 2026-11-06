"use client";

import { RouteErrorState } from "@/components/route-error-state";

export default function ErrorBoundary({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  // retry re-fetches the failed segment; reset would only re-render it.
  retry: () => void;
}) {
  return (
    <RouteErrorState
      error={error}
      retry={retry}
      title="The page could not load."
    />
  );
}

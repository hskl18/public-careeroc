"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function RetryJobButton({ jobId, proxyPrefix }: { jobId: string; proxyPrefix: string }) {
  const router = useRouter();
  const [state, setState] = useState<"idle" | "pending" | "error">("idle");

  async function retry() {
    setState("pending");
    const response = await fetch(`${proxyPrefix}/api/admin/email-processing-jobs/${jobId}/retry`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ confirmation: "retry" }),
    });
    if (!response.ok) {
      setState("error");
      return;
    }

    setState("idle");
    router.refresh();
  }

  return (
    <button
      type="button"
      onClick={() => void retry()}
      disabled={state === "pending"}
      className="btn btn-secondary btn-sm"
    >
      {state === "pending" ? "Retrying..." : state === "error" ? "Retry failed, try again" : "Retry"}
    </button>
  );
}

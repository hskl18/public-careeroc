"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { ResumeFit } from "@/lib/resumes";
import { readErrorMessage } from "@/lib/client-fetch";

type ResumeFitPanelProps = {
  applicationId: string;
  proxyPrefix: string | null;
};

function normalizeFit(payload: Partial<ResumeFit>): ResumeFit {
  return {
    jobApplicationId: payload.jobApplicationId ?? "",
    resumeVersionId: payload.resumeVersionId ?? null,
    matchScore: Number(payload.matchScore ?? 0),
    strengths: Array.isArray(payload.strengths) ? payload.strengths : [],
    gaps: Array.isArray(payload.gaps) ? payload.gaps : [],
    rationale: payload.rationale ?? "",
  };
}

export function ResumeFitPanel({ applicationId, proxyPrefix }: ResumeFitPanelProps) {
  // The API caches each fit per resume version, so a new resume shows up on the next visit.
  const [fit, setFit] = useState<ResumeFit | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!proxyPrefix) {
      return;
    }

    let cancelled = false;

    async function loadFit() {
      setLoading(true);
      setError(null);
      try {
        const response = await fetch(`${proxyPrefix}/api/applications/${applicationId}/resume-fit`, {
          cache: "no-store",
        });
        if (response.status === 404) {
          if (!cancelled) {
            setFit(null);
            setError("Not available yet.");
          }
          return;
        }
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
        const nextFit = normalizeFit((await response.json()) as Partial<ResumeFit>);
        if (!cancelled) {
          setFit(nextFit);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Resume fit failed.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadFit();

    return () => {
      cancelled = true;
    };
  }, [applicationId, proxyPrefix]);

  const displayLoading = Boolean(proxyPrefix) && loading;

  return (
    <section className="card p-4">
      <div className="flex items-baseline justify-between gap-3">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">
          <Link href="/app/resume" className="hover:underline max-sm:inline-flex max-sm:min-h-6 max-sm:items-center">
            Role match
          </Link>
        </h2>
        <span className="text-base font-semibold tabular-nums text-[var(--text-primary)]">
          {displayLoading ? "…" : fit ? `${Math.round(fit.matchScore)}%` : "—"}
        </span>
      </div>

      {displayLoading ? (
        <div className="mt-4 grid gap-2">
          <div className="h-3 animate-pulse rounded-full bg-[var(--surface-overlay)]" />
          <div className="h-3 w-2/3 animate-pulse rounded-full bg-[var(--surface-overlay)]" />
        </div>
      ) : error ? (
        <p className="mt-3 text-sm leading-6 text-[var(--text-tertiary)]">{error}</p>
      ) : fit ? (
        <ul className="mt-1 divide-y divide-[var(--border)]">
          {fit.gaps.slice(0, 3).map((gap) => (
            <li key={gap} className="py-1.5 text-sm text-[var(--text-secondary)]">
              {gap}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

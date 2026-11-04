"use client";

import Link from "next/link";
import type { Route } from "next";
import { useEffect, useState } from "react";
import type { ResumeFit } from "@/lib/resumes";

type ResumeFitPanelProps = {
  applicationId: string;
  proxyPrefix: string | null;
};

const resumeFitCache = new Map<string, ResumeFit>();

async function readErrorMessage(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

function normalizeFit(payload: Partial<ResumeFit>): ResumeFit {
  return {
    jobApplicationId: payload.jobApplicationId ?? "",
    resumeVersionId: payload.resumeVersionId ?? null,
    matchScore: Number(payload.matchScore ?? 0),
    strengths: Array.isArray(payload.strengths) ? payload.strengths : [],
    gaps: Array.isArray(payload.gaps) ? payload.gaps : [],
    rationale: payload.rationale ?? null,
  };
}

export function ResumeFitPanel({ applicationId, proxyPrefix }: ResumeFitPanelProps) {
  const [fit, setFit] = useState<ResumeFit | null>(
    () => resumeFitCache.get(applicationId) ?? null,
  );
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const cachedFit = resumeFitCache.get(applicationId);
    if (cachedFit) {
      queueMicrotask(() => setFit(cachedFit));
      return;
    }

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
            setError("Resume fit is not available until a parsed resume and role evidence exist.");
          }
          return;
        }
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
        const nextFit = normalizeFit((await response.json()) as Partial<ResumeFit>);
        resumeFitCache.set(applicationId, nextFit);
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

  const resumeHref = `/app/resume?applicationId=${encodeURIComponent(applicationId)}` as Route;
  const displayLoading = Boolean(proxyPrefix) && loading;

  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="eyebrow">Resume fit</p>
          <h2 className="mt-1.5 text-base font-semibold text-[var(--text-primary)]">
            Role match
          </h2>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          {displayLoading ? "Computing" : fit ? `${Math.round(fit.matchScore)}%` : "Unavailable"}
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
        <div className="mt-4 grid gap-4">
          <div>
            <p className="text-3xl font-semibold text-[var(--text-primary)]">
              {Math.round(fit.matchScore)}%
            </p>
            {fit.rationale ? (
              <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
                {fit.rationale}
              </p>
            ) : null}
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <ResumeFitList title="Strengths" items={fit.strengths.slice(0, 3)} tone="success" />
            <ResumeFitList title="Gaps" items={fit.gaps.slice(0, 3)} tone="warning" />
          </div>
          <Link href={resumeHref} className="btn btn-secondary btn-sm w-fit">
            Tailor resume for this role
          </Link>
        </div>
      ) : null}
    </section>
  );
}

function ResumeFitList({
  title,
  items,
  tone,
}: {
  title: string;
  items: string[];
  tone: "success" | "warning";
}) {
  const colorClass = tone === "success" ? "text-[var(--green)]" : "text-[var(--yellow)]";

  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
      <p className={`eyebrow ${colorClass}`}>{title}</p>
      {items.length === 0 ? (
        <p className="mt-2 text-sm text-[var(--text-tertiary)]">No signals returned.</p>
      ) : (
        <ul className="mt-2 grid gap-2 text-sm leading-6 text-[var(--text-secondary)]">
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

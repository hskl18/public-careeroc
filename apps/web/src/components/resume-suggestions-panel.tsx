"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { ResumeSuggestion } from "@/lib/resumes";

type ResumeSuggestionsPanelProps = {
  resumeDocumentId: string;
  proxyPrefix: string | null;
  applicationId?: string | null;
};

async function readErrorMessage(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

function normalizeSuggestion(payload: Partial<ResumeSuggestion>): ResumeSuggestion {
  return {
    id: payload.id ?? "",
    kind: payload.kind ?? null,
    targetSection: payload.targetSection ?? null,
    originalText: payload.originalText ?? null,
    suggestedText: payload.suggestedText ?? null,
    rationale: payload.rationale ?? null,
    evidenceLinks: Array.isArray(payload.evidenceLinks) ? payload.evidenceLinks : [],
  };
}

export function ResumeSuggestionsPanel({
  resumeDocumentId,
  proxyPrefix,
  applicationId,
}: ResumeSuggestionsPanelProps) {
  const router = useRouter();
  const [suggestions, setSuggestions] = useState<ResumeSuggestion[]>([]);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const loadedCount = suggestions.length;
  const heading = useMemo(
    () => (applicationId ? "Role-focused suggestions" : "Resume suggestions"),
    [applicationId],
  );

  useEffect(() => {
    if (!proxyPrefix) {
      return;
    }

    let cancelled = false;

    async function loadSuggestions() {
      setLoading(true);
      setError(null);
      try {
        const response = await fetch(
          `${proxyPrefix}/api/resumes/${resumeDocumentId}/suggestions`,
          { cache: "no-store" },
        );
        if (response.status === 404) {
          if (!cancelled) {
            setSuggestions([]);
          }
          return;
        }
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
        const payload = (await response.json()) as Partial<ResumeSuggestion>[];
        if (!cancelled) {
          setSuggestions(payload.map(normalizeSuggestion).filter((item) => item.id));
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Suggestions failed.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadSuggestions();

    return () => {
      cancelled = true;
    };
  }, [resumeDocumentId, proxyPrefix]);

  const displayLoading = Boolean(proxyPrefix) && loading;

  async function resolveSuggestion(suggestion: ResumeSuggestion, action: "accept" | "decline") {
    if (!proxyPrefix || busyId !== null) return;

    setBusyId(suggestion.id);
    setError(null);
    setNotice(null);
    setSuggestions((current) => current.filter((item) => item.id !== suggestion.id));

    try {
      const response = await fetch(
        `${proxyPrefix}/api/resumes/${resumeDocumentId}/suggestions/${suggestion.id}/${action}`,
        { method: "POST" },
      );
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      setNotice(action === "accept" ? "Suggestion accepted." : "Suggestion rejected.");
      if (action === "accept") {
        router.refresh();
      }
    } catch (resolveError) {
      setSuggestions((current) =>
        current.some((item) => item.id === suggestion.id)
          ? current
          : [suggestion, ...current],
      );
      setError(
        resolveError instanceof Error
          ? resolveError.message
          : "Couldn't update suggestion.",
      );
    } finally {
      setBusyId(null);
    }
  }

  async function acceptAll() {
    if (!proxyPrefix || suggestions.length === 0 || busyId !== null) return;

    setBusyId("accept-all");
    setError(null);
    setNotice(null);
    const snapshot = suggestions;
    setSuggestions([]);

    try {
      for (const suggestion of snapshot) {
        const response = await fetch(
          `${proxyPrefix}/api/resumes/${resumeDocumentId}/suggestions/${suggestion.id}/accept`,
          { method: "POST" },
        );
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
      }
      setNotice("All suggestions accepted.");
      router.refresh();
    } catch (acceptError) {
      setSuggestions(snapshot);
      setError(acceptError instanceof Error ? acceptError.message : "Couldn't accept all.");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.92)] p-4 sm:p-5">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3 border-b border-[var(--border)] pb-3">
        <div>
          <p className="eyebrow">Diff suggestions</p>
          <h3 className="mt-1 text-base font-semibold text-[var(--text-primary)]">
            {heading}
          </h3>
          {applicationId ? (
            <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
              Opened from application {applicationId.slice(0, 8)}. Backend currently
              returns the saved document suggestions for this resume.
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className="badge bg-[var(--surface-elevated)] text-[var(--text-secondary)]">
            {displayLoading ? "Loading" : `${loadedCount} open`}
          </span>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            disabled={!proxyPrefix || busyId !== null || suggestions.length === 0}
            onClick={() => void acceptAll()}
          >
            {busyId === "accept-all" ? "Accepting..." : "Accept all"}
          </button>
        </div>
      </div>

      {notice ? <p className="mb-3 text-sm font-medium text-[var(--green)]">{notice}</p> : null}
      {error ? <p className="mb-3 text-sm font-medium text-[var(--red)]">{error}</p> : null}

      {displayLoading ? (
        <div className="grid gap-2">
          <div className="h-3 animate-pulse rounded-full bg-[var(--surface-overlay)]" />
          <div className="h-3 w-3/4 animate-pulse rounded-full bg-[var(--surface-overlay)]" />
        </div>
      ) : suggestions.length === 0 ? (
        <p className="text-sm leading-6 text-[var(--text-tertiary)]">
          No open resume suggestions.
        </p>
      ) : (
        <div className="grid gap-3">
          {suggestions.map((suggestion) => (
            <article key={suggestion.id} className="card-elevated p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="eyebrow">
                    {suggestion.targetSection ?? suggestion.kind ?? "Resume edit"}
                  </p>
                  {suggestion.rationale ? (
                    <p className="mt-1 text-sm leading-6 text-[var(--text-secondary)]">
                      {suggestion.rationale}
                    </p>
                  ) : null}
                </div>
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                  {suggestion.kind ?? "suggestion"}
                </span>
              </div>
              <div className="mt-3 grid gap-2">
                {suggestion.originalText ? (
                  <p className="rounded-md border border-[var(--red-strong)]/25 bg-[var(--red-soft)] px-3 py-2 text-sm leading-6 text-[var(--text-secondary)] line-through decoration-[var(--red)]">
                    {suggestion.originalText}
                  </p>
                ) : null}
                {suggestion.suggestedText ? (
                  <p className="rounded-md border border-[var(--green)]/25 bg-[var(--green-soft)] px-3 py-2 text-sm font-medium leading-6 text-[var(--text-primary)] underline decoration-[var(--green)] decoration-2 underline-offset-4">
                    {suggestion.suggestedText}
                  </p>
                ) : null}
              </div>
              {suggestion.evidenceLinks.length > 0 ? (
                <p className="mt-2 break-all text-xs leading-5 text-[var(--text-tertiary)]">
                  Evidence: {suggestion.evidenceLinks.slice(0, 3).join(" · ")}
                </p>
              ) : null}
              <div className="mt-3 flex flex-wrap gap-2">
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  disabled={!proxyPrefix || busyId !== null}
                  onClick={() => void resolveSuggestion(suggestion, "accept")}
                >
                  {busyId === suggestion.id ? "Saving..." : "Accept"}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={!proxyPrefix || busyId !== null}
                  onClick={() => void resolveSuggestion(suggestion, "decline")}
                >
                  Reject
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

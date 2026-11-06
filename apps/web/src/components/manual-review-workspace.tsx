"use client";

import Link from "next/link";
import { useDeferredValue, useState } from "react";
import { ManualReviewEmailDetail } from "@/components/manual-review-email-detail";
import { fetchWithInFlightDedup, readErrorMessage } from "@/lib/client-fetch";
import {
  formatTimestampInTimeZone,
  toUtcIsoStringFromDateTimeLocal,
} from "@/lib/date-time";
import type {
  ManualReviewResponse,
  ReviewEmail,
} from "@/lib/manual-review";
import { formatEnumLabel } from "@/lib/labels";

type ManualReviewWorkspaceProps = {
  initialReview: ManualReviewResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
  timeZoneId: string;
  hasConnectedAccount?: boolean;
  hasSyncedOnce?: boolean;
};

function formatTimestamp(value: string, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "");
}


function containsText(value: string | null | undefined, query: string) {
  if (!value) return false;
  return value.toLowerCase().includes(query);
}

export function ManualReviewWorkspace({
  initialReview,
  source,
  proxyPrefix,
  timeZoneId,
  hasConnectedAccount = true,
  hasSyncedOnce = true,
}: ManualReviewWorkspaceProps) {
  const [review, setReview] = useState(initialReview);
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("all");
  const [selectedEmailId, setSelectedEmailId] = useState<string | null>(
    initialReview.emails[0]?.id ?? null,
  );
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const canPersist = Boolean(proxyPrefix) && source === "api";
  const deferredSearchQuery = useDeferredValue(searchQuery);
  const normalizedSearchQuery = deferredSearchQuery.trim().toLowerCase();
  const filtersActive =
    normalizedSearchQuery.length > 0 || selectedCategory !== "all";
  const coldStart =
    source === "api" &&
    review.emails.length === 0 &&
    (!hasConnectedAccount || !hasSyncedOnce);
  const filteredEmails = review.emails.filter((e) => {
    const matchesCat =
      selectedCategory === "all"
        ? true
        : selectedCategory === "__unclassified__"
          ? e.category === null
          : e.category === selectedCategory;
    if (!matchesCat) return false;
    if (!normalizedSearchQuery) return true;
    return (
      containsText(e.sender, normalizedSearchQuery) ||
      containsText(e.subject, normalizedSearchQuery) ||
      containsText(e.snippet, normalizedSearchQuery) ||
      containsText(e.bodyPreview, normalizedSearchQuery) ||
      containsText(e.category, normalizedSearchQuery) ||
      containsText(e.extractedCompany, normalizedSearchQuery) ||
      containsText(e.extractedRole, normalizedSearchQuery) ||
      containsText(e.contactName, normalizedSearchQuery) ||
      containsText(e.contactEmail, normalizedSearchQuery) ||
      containsText(e.contactType, normalizedSearchQuery) ||
      containsText(e.contactRoleHint, normalizedSearchQuery) ||
      containsText(e.reviewReason, normalizedSearchQuery) ||
      containsText(e.notes, normalizedSearchQuery)
    );
  });
  const selectedEmail =
    filteredEmails.find((email) => email.id === selectedEmailId) ??
    filteredEmails[0] ??
    null;

  async function refreshReviewFromApi() {
    if (!proxyPrefix) return false;
    const response = await fetchWithInFlightDedup(`${proxyPrefix}/api/review`, {
      cache: "no-store",
    });
    if (!response.ok) return false;
    setReview((await response.json()) as ManualReviewResponse);
    return true;
  }

  async function handleRefreshReview() {
    if (!proxyPrefix) { setError("API proxy is not configured."); return; }
    setPendingKey("refresh"); setError(null); setNotice(null);
    try {
      const refreshed = await refreshReviewFromApi();
      if (!refreshed) throw new Error("Failed to refresh the review queue.");
      setNotice("Refreshed the review queue.");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to refresh.");
    } finally { setPendingKey(null); }
  }

  async function handleEmailSubmit(event: React.FormEvent<HTMLFormElement>, email: ReviewEmail) {
    event.preventDefault();
    if (!proxyPrefix) { setError("API proxy is not configured."); return; }
    const fd = new FormData(event.currentTarget);
    const category = String(fd.get("category") ?? email.category ?? "");
    const selectedApplicationId = String(fd.get("jobApplicationId") ?? (email.jobApplicationId ? email.jobApplicationId : "__auto__"));
    const company = String(fd.get("company") ?? email.extractedCompany ?? "").trim();
    const role = String(fd.get("role") ?? email.extractedRole ?? "").trim();
    const notes = String(fd.get("notes") ?? email.notes ?? "").trim();
    const dueDateValue = String(fd.get("dueDateUtc") ?? "");
    const dueDateUtc = dueDateValue
      ? toUtcIsoStringFromDateTimeLocal(dueDateValue, timeZoneId)
      : undefined;
    const rk = `email:${email.id}`;
    setPendingKey(rk); setError(null); setNotice(null);
    try {
      if (dueDateValue && !dueDateUtc) {
        throw new Error("Invalid due date.");
      }

      const response = await fetch(`${proxyPrefix}/api/emails/${email.id}/correction`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          category,
          clearApplicationAssignment: selectedApplicationId === "__none__",
          jobApplicationId: selectedApplicationId !== "__none__" && selectedApplicationId !== "__auto__" ? selectedApplicationId : undefined,
          company, role,
          actionRequired: fd.get("actionRequired") === "on",
          dueDateUtc,
          notes,
        }),
      });
      if (!response.ok) throw new Error(await readErrorMessage(response));
      const refreshed = await refreshReviewFromApi();
      if (!refreshed) setReview((c) => ({ ...c, emails: c.emails.filter((x) => x.id !== email.id) }));
      setNotice(`Saved corrections for "${email.subject}".`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update email.");
    } finally { setPendingKey(null); }
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4 animate-in">
      <h1 className="sr-only">Email review</h1>
      <section className="shrink-0">
        {coldStart ? (
          <div className="card-elevated p-4">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {hasConnectedAccount
                ? "Waiting on your first Gmail sync"
                : "No Gmail connected yet"}
            </p>
            <div className="mt-2 flex flex-wrap gap-2">
              {hasConnectedAccount ? null : (
                <Link
                  href="/app/inbox#connect-inbox"
                  className="btn btn-primary btn-sm"
                >
                  Connect Gmail
                </Link>
              )}
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap items-center gap-2">
            <input
              aria-label="Search review items"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search"
              className="input min-w-0 flex-1"
            />
            <select
              aria-label="Category"
              value={selectedCategory}
              onChange={(e) => setSelectedCategory(e.target.value)}
              className="input w-44"
            >
              <option value="all">All categories</option>
              <option value="__unclassified__">Unclassified</option>
              {review.categoryOptions.map((c) => (
                <option key={c} value={c}>{formatEnumLabel(c)}</option>
              ))}
            </select>
            {filtersActive ? (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => { setSearchQuery(""); setSelectedCategory("all"); }}
              >
                Clear
              </button>
            ) : null}
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => void handleRefreshReview()}
              disabled={!canPersist || pendingKey !== null}
            >
              {pendingKey === "refresh" ? <><span className="spinner" /> Refreshing...</> : "Refresh"}
            </button>
          </div>
        )}

        {!canPersist && (
          <div role="status" className="alert alert-warning mt-2">
            API offline.
          </div>
        )}
        {notice ? <div className="alert alert-success mt-3">{notice}</div> : null}
        <p role="status" className="sr-only">{notice ?? ""}</p>
        {error && <div role="alert" className="alert alert-error mt-3">{error}</div>}
      </section>

      <div className="grid min-h-0 flex-1 gap-3 [grid-template-rows:minmax(0,1fr)] md:[grid-template-rows:minmax(0,2fr)_minmax(0,3fr)] xl:grid-cols-[0.74fr_1.26fr] xl:[grid-template-rows:minmax(0,1fr)]">
        <div className="workspace-scroll-region min-h-0 pr-1">
          <section className="card min-h-full p-0">
            <div className="flex items-center justify-between gap-3 px-4 pt-3">
              <h2 className="text-base font-semibold text-[var(--text-primary)]">Emails</h2>
              <span className="text-xs tabular-nums text-[var(--text-tertiary)]">
                {filteredEmails.length}
              </span>
            </div>

            {filteredEmails.length === 0 ? (
              <p className="px-4 pb-3 pt-2 text-sm text-[var(--text-tertiary)]">
                {review.emails.length === 0
                  ? coldStart
                    ? "Nothing yet."
                    : "Queue is clear."
                  : "No matches."}
              </p>
            ) : (
              <div className="mt-1 divide-y divide-[var(--border)]">
                {filteredEmails.map((email) => {
                  const selected = selectedEmail?.id === email.id;

                  return (
                    <button
                      key={email.id}
                      type="button"
                      aria-pressed={selected}
                      className={`w-full px-4 py-2.5 text-left transition hover:bg-[var(--surface-overlay)] ${
                        selected ? "shadow-[inset_4px_0_0_var(--accent)]" : ""
                      }`}
                      onClick={() => setSelectedEmailId(email.id)}
                    >
                      <span className="block text-sm font-medium text-[var(--text-primary)]">{email.subject}</span>
                      <span className="mt-0.5 flex items-baseline justify-between gap-3 text-xs text-[var(--text-tertiary)]">
                        <span className="min-w-0 truncate">{email.sender}</span>
                        <span className="shrink-0">{formatTimestamp(email.receivedAtUtc, timeZoneId)}</span>
                      </span>
                    </button>
                  );
                })}
              </div>
            )}
          </section>
        </div>

        <div className="workspace-scroll-region min-h-0 pr-1">
          <ManualReviewEmailDetail
            email={selectedEmail}
            applications={review.applications}
            categoryOptions={review.categoryOptions}
            timeZoneId={timeZoneId}
            canPersist={canPersist}
            pendingKey={pendingKey}
            onSubmit={(event, email) => void handleEmailSubmit(event, email)}
          />
        </div>
      </div>

    </div>
  );
}

"use client";

import Link from "next/link";
import { useDeferredValue, useState } from "react";
import {
  AgentTraceStepsPanel,
  AgentTraceSummaryCard,
} from "@/components/application-thread-evidence-section";
import { ManualReviewEmailDetail } from "@/components/manual-review-email-detail";
import { StatusPill } from "@/components/status-pill";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import {
  formatTimestampInTimeZone,
  toUtcIsoStringFromDateTimeLocal,
} from "@/lib/date-time";
import type {
  ManualReviewResponse,
  ReviewArtifactEvidence,
  ReviewApplication,
  ReviewEmail,
} from "@/lib/manual-review";
import { formatEnumLabel } from "@/lib/labels";

type ManualReviewWorkspaceProps = {
  initialReview: ManualReviewResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
  currentUserEmail: string;
  currentUserName?: string | null;
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

function ApplicationOverridesSection({
  applications,
  statusOptions,
  pendingKey,
  canPersist,
  timeZoneId,
  onSubmit,
}: {
  applications: ReviewApplication[];
  statusOptions: string[];
  pendingKey: string | null;
  canPersist: boolean;
  timeZoneId: string;
  onSubmit: (
    event: React.FormEvent<HTMLFormElement>,
    application: ReviewApplication,
  ) => void;
}) {
  return (
    <section className="card p-5">
      <details>
        <summary className="flex cursor-pointer items-center justify-between gap-3 list-none">
          <p className="eyebrow">Application overrides</p>
          <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
            {applications.length}
          </span>
        </summary>

      {applications.length === 0 ? (
        <p className="mt-3 text-sm text-[var(--text-tertiary)]">
          No application records.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          {applications.map((application) => {
            const formKey = [
              application.id,
              application.status,
              application.priority,
              application.lastActivityAtUtc,
            ].join(":");

            return (
              <article key={formKey} className="card-elevated p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="text-base font-semibold text-[var(--text-primary)]">
                      {application.company}
                    </h3>
                    <p className="text-sm text-[var(--text-secondary)]">
                      {application.role}
                    </p>
                  </div>
                  <StatusPill status={application.status} />
                </div>

                <p className="mt-2 text-xs text-[var(--text-tertiary)]">
                  Last activity {formatTimestamp(application.lastActivityAtUtc, timeZoneId)}
                </p>

                <form
                  className="mt-4 space-y-3"
                  onSubmit={(event) => onSubmit(event, application)}
                >
                  <label className="block">
                    <span className="eyebrow block mb-1.5">Stage</span>
                    <select
                      name="status"
                      defaultValue={application.status}
                      className="input"
                      disabled={!canPersist || pendingKey !== null}
                    >
                      {statusOptions.map((status) => (
                        <option key={status} value={status}>
                          {formatEnumLabel(status)}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="block">
                    <span className="eyebrow block mb-1.5">Priority</span>
                    <input
                      name="priority"
                      type="number"
                      min={1}
                      max={5}
                      defaultValue={application.priority}
                      className="input"
                      disabled={!canPersist || pendingKey !== null}
                    />
                  </label>

                  <button
                    type="submit"
                    className="btn btn-secondary btn-block"
                    disabled={
                      !canPersist ||
                      pendingKey === `application:${application.id}` ||
                      pendingKey !== null
                    }
                  >
                    {pendingKey === `application:${application.id}`
                      ? <><span className="spinner" /> Saving...</>
                      : "Save override"}
                  </button>
                </form>
              </article>
            );
          })}
        </div>
      )}
      </details>
    </section>
  );
}

function ArtifactReviewSection({
  artifacts,
  canPersist,
  pendingKey,
  timeZoneId,
  onReview,
}: {
  artifacts: ReviewArtifactEvidence[];
  canPersist: boolean;
  pendingKey: string | null;
  timeZoneId: string;
  onReview: (
    artifact: ReviewArtifactEvidence,
    outcome: "keep_evidence" | "dismiss_evidence",
  ) => void;
}) {
  return (
    <section className="card p-5">
      <div className="flex items-center justify-between gap-3">
        <p className="eyebrow">Artifact reviews</p>
        <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
          {artifacts.length}
        </span>
      </div>

      {artifacts.length === 0 ? (
        <p className="mt-3 text-sm text-[var(--text-tertiary)]">
          No artifact evidence is waiting for review.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          {artifacts.map((artifact) => (
            <article key={artifact.id} className="card-elevated p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-[11px] font-mono uppercase tracking-[0.14em] text-[var(--text-tertiary)]">
                    {formatEnumLabel(artifact.artifactKind)}
                  </p>
                  <h3 className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
                    {artifact.safeTitle}
                  </h3>
                  <p className="mt-1 text-xs text-[var(--text-secondary)]">
                    {artifact.company} / {artifact.role}
                  </p>
                </div>
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                  {(artifact.confidence * 100).toFixed(0)}%
                </span>
              </div>

              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                {artifact.reviewReason}
              </p>
              <div className="mt-3 rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-3">
                <p className="eyebrow">Extracted {formatEnumLabel(artifact.extractedFactType)}</p>
                <p className="mt-1 text-sm font-medium leading-6 text-[var(--text-primary)]">
                  {artifact.extractedFactText}
                </p>
                <p className="mt-2 text-xs leading-5 text-[var(--text-tertiary)]">
                  {artifact.sourceSnippet}
                </p>
              </div>

              <div className="mt-3">
                <AgentTraceSummaryCard
                  summary={artifact.traceSummary}
                  title="Artifact model trace"
                  compact
                />
              </div>
              <div className="mt-3">
                <AgentTraceStepsPanel
                  steps={artifact.agentTrace?.steps ?? []}
                  title="Artifact step trace"
                />
              </div>

              <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
                <span className="text-xs text-[var(--text-tertiary)]">
                  Added {formatTimestamp(artifact.createdAtUtc, timeZoneId)}
                </span>
                <div className="flex flex-wrap gap-2">
                  <Link
                    href={`/app/applications/${artifact.jobApplicationId}`}
                    className="btn btn-secondary btn-sm"
                  >
                    Open application
                  </Link>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={!canPersist || pendingKey !== null}
                    onClick={() => onReview(artifact, "dismiss_evidence")}
                  >
                    {pendingKey === `artifact:${artifact.id}:dismiss_evidence`
                      ? <><span className="spinner" /> Dismissing...</>
                      : "Dismiss"}
                  </button>
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    disabled={!canPersist || pendingKey !== null}
                    onClick={() => onReview(artifact, "keep_evidence")}
                  >
                    {pendingKey === `artifact:${artifact.id}:keep_evidence`
                      ? <><span className="spinner" /> Keeping...</>
                      : "Keep evidence"}
                  </button>
                </div>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
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
  const totalWaiting = review.totalWaiting ?? review.emails.length;
  const totalUnassigned =
    review.totalUnassigned ??
    review.emails.filter((email) => !email.jobApplicationId).length;
  const reviewArtifacts = review.artifacts ?? [];
  const totalArtifactReviews =
    review.totalArtifactReviews ?? reviewArtifacts.length;
  const coldStart =
    source === "api" &&
    review.emails.length === 0 &&
    (!hasConnectedAccount || !hasSyncedOnce);
  const coldStartHint = !hasConnectedAccount
    ? "Connect your Gmail in Inbox to start queueing evidence."
    : "Once your first sync finishes, anything we're unsure about lands here for your call.";

  const filteredApplications = review.applications.filter((a) => {
    if (!normalizedSearchQuery) return true;
    return (
      containsText(a.company, normalizedSearchQuery) ||
      containsText(a.role, normalizedSearchQuery) ||
      containsText(a.status, normalizedSearchQuery)
    );
  });

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
  const filteredArtifacts = reviewArtifacts.filter((artifact) => {
    if (!normalizedSearchQuery) return true;
    return (
      containsText(artifact.company, normalizedSearchQuery) ||
      containsText(artifact.role, normalizedSearchQuery) ||
      containsText(artifact.safeTitle, normalizedSearchQuery) ||
      containsText(artifact.extractedFactType, normalizedSearchQuery) ||
      containsText(artifact.extractedFactText, normalizedSearchQuery) ||
      containsText(artifact.sourceSnippet, normalizedSearchQuery) ||
      containsText(artifact.reviewReason, normalizedSearchQuery)
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

  async function handleApplicationSubmit(event: React.FormEvent<HTMLFormElement>, application: ReviewApplication) {
    event.preventDefault();
    if (!proxyPrefix) { setError("API proxy is not configured."); return; }
    const fd = new FormData(event.currentTarget);
    const status = String(fd.get("status") ?? application.status);
    const priority = Number(fd.get("priority") ?? application.priority);
    const rk = `application:${application.id}`;
    setPendingKey(rk); setError(null); setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/applications/${application.id}/status`, {
        method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status, priority }),
      });
      if (!response.ok) throw new Error(`Request failed with ${response.status}`);
      setReview((c) => ({
        ...c,
        applications: c.applications.map((x) => x.id === application.id ? { ...x, status, priority, lastActivityAtUtc: new Date().toISOString() } : x),
      }));
      setNotice(`Updated ${application.company} / ${application.role}.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update application.");
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
      if (!response.ok) throw new Error(`Request failed with ${response.status}`);
      const refreshed = await refreshReviewFromApi();
      if (!refreshed) setReview((c) => ({ ...c, emails: c.emails.filter((x) => x.id !== email.id) }));
      setNotice(`Saved corrections for "${email.subject}".`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update email.");
    } finally { setPendingKey(null); }
  }

  async function handleArtifactReview(
    artifact: ReviewArtifactEvidence,
    outcome: "keep_evidence" | "dismiss_evidence",
  ) {
    if (!proxyPrefix) { setError("API proxy is not configured."); return; }
    const pendingArtifactKey = `artifact:${artifact.id}:${outcome}`;
    setPendingKey(pendingArtifactKey); setError(null); setNotice(null);
    try {
      const response = await fetch(
        `${proxyPrefix}/api/applications/${artifact.jobApplicationId}/artifacts/${artifact.id}/review`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ outcome }),
        },
      );
      if (!response.ok) throw new Error(`Request failed with ${response.status}`);
      const refreshed = await refreshReviewFromApi();
      if (!refreshed) {
        setReview((current) => ({
          ...current,
          artifacts: (current.artifacts ?? []).filter((item) => item.id !== artifact.id),
          totalArtifactReviews: Math.max(0, (current.totalArtifactReviews ?? 1) - 1),
          totalWaiting: Math.max(0, (current.totalWaiting ?? 1) - 1),
        }));
      }
      setNotice(
        outcome === "dismiss_evidence"
          ? `Dismissed "${artifact.safeTitle}".`
          : `Kept "${artifact.safeTitle}" as reviewed evidence.`,
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to review artifact evidence.");
    } finally { setPendingKey(null); }
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4 animate-in">
      <section className="card shrink-0 p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex min-w-0 items-center gap-3">
            <div className="min-w-0">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <Link href="/app" className="btn btn-secondary btn-sm">
                  Back to dashboard
                </Link>
              </div>
              <div className="flex flex-wrap items-baseline gap-3">
                <p className="eyebrow">Email review queue</p>
                <h2 className="text-base font-semibold text-[var(--text-primary)]">
                  Blocked email evidence
                </h2>
              </div>
              <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                {coldStart
                  ? coldStartHint
                  : review.emails.length === 0
                    ? "Queue is clear."
                    : `${totalWaiting} waiting · ${totalUnassigned} unassigned · ${totalArtifactReviews} artifacts · showing ${review.emails.length}`}
              </p>
            </div>
          </div>
          <span
            className={`badge ${
              source === "api"
                ? "bg-[var(--green-soft)] text-[var(--green)]"
                : "bg-[var(--yellow-soft)] text-[var(--yellow)]"
            }`}
          >
            {source === "api" ? "Live" : "Offline"}
          </span>
        </div>

        {coldStart ? (
          <div className="mt-3 card-elevated p-4">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {hasConnectedAccount
                ? "Waiting on your first Gmail sync"
                : "No Gmail connected yet"}
            </p>
            <p className="mt-1 text-xs leading-6 text-[var(--text-tertiary)]">
              The review queue surfaces emails where the classifier wasn&rsquo;t
              confident enough to auto-update your pipeline. You decide the call.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasConnectedAccount ? (
                <Link href="/app" className="btn btn-secondary btn-sm">
                  Back to dashboard
                </Link>
              ) : (
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
          <div className="mt-3">
            <div className="grid gap-2 md:grid-cols-[1fr_auto_auto_auto] md:items-end">
              <label className="block">
                <span className="eyebrow mb-1.5 block">Search review items</span>
                <input
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Search sender, subject, company, notes..."
                  className="input"
                />
              </label>
              <label className="block">
                <span className="eyebrow mb-1.5 block">Category</span>
                <select
                  value={selectedCategory}
                  onChange={(e) => setSelectedCategory(e.target.value)}
                  className="input"
                >
                  <option value="all">All categories</option>
                  <option value="__unclassified__">Unclassified</option>
                  {review.categoryOptions.map((c) => (
                    <option key={c} value={c}>{formatEnumLabel(c)}</option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => { setSearchQuery(""); setSelectedCategory("all"); }}
                disabled={!filtersActive}
              >
                Clear
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => void handleRefreshReview()}
                disabled={!canPersist || pendingKey !== null}
              >
                {pendingKey === "refresh" ? <><span className="spinner" /> Refreshing...</> : "Refresh"}
              </button>
            </div>
          </div>
        )}

        {!canPersist && (
          <div className="alert alert-warning mt-4">
            Offline - corrections disabled until the API reconnects.
          </div>
        )}
        {notice && <div className="alert alert-success mt-3">{notice}</div>}
        {error && <div className="alert alert-error mt-3">{error}</div>}
      </section>

      <div className="grid min-h-0 flex-1 gap-4 [grid-template-rows:minmax(0,1fr)] xl:grid-cols-[0.74fr_1.26fr]">
        <div className="workspace-scroll-region min-h-0 pr-1">
          <section className="card p-5">
            <div className="flex items-center justify-between gap-3">
              <p className="eyebrow">Blocked threads</p>
              <span className="badge bg-[var(--red-soft)] text-[var(--red)]">
                {filteredEmails.length}
              </span>
            </div>

            {filteredEmails.length === 0 ? (
              <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                {review.emails.length === 0
                  ? coldStart
                    ? "Queue will fill up after your first sync."
                    : "Queue is clear."
                  : "No matches - adjust filters."}
              </p>
            ) : (
              <div className="mt-4 space-y-3">
                {filteredEmails.map((email) => {
                  const selected = selectedEmail?.id === email.id;

                  return (
                    <button
                      key={email.id}
                      type="button"
                      className={`card-elevated w-full p-4 text-left ${
                        selected
                          ? "border-[var(--border-active)] bg-[var(--surface)] shadow-[0_0_0_1px_var(--border-active)]"
                          : ""
                      }`}
                      onClick={() => setSelectedEmailId(email.id)}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <p className="text-[11px] font-mono text-[var(--text-tertiary)]">
                            {email.sender}
                          </p>
                          <h3 className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
                            {email.subject}
                          </h3>
                          <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
                            {email.reviewReason ?? "Awaiting manual review."}
                          </p>
                        </div>
                        <div className="flex shrink-0 flex-col items-end gap-1.5">
                          <span className="badge bg-[var(--red-soft)] text-[var(--red)]">
                            Review
                          </span>
                          <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                            {formatTimestamp(email.receivedAtUtc, timeZoneId)}
                          </span>
                        </div>
                      </div>

                      <div className="mt-3 flex flex-wrap gap-1.5">
                        {email.category ? (
                          <span className="badge bg-[var(--surface-overlay)] text-[var(--text-secondary)]">
                            {formatEnumLabel(email.category)}
                          </span>
                        ) : (
                          <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                            Unclassified
                          </span>
                        )}
                        {email.jobApplicationId ? (
                          <span className="badge bg-[var(--accent-soft)] text-[var(--accent)]">
                            Assigned
                          </span>
                        ) : (
                          <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                            Unassigned
                          </span>
                        )}
                        {email.threadMessages.length > 1 ? (
                          <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                            {email.threadMessages.length} messages in thread
                          </span>
                        ) : null}
                      </div>
                    </button>
                  );
                })}
              </div>
            )}
          </section>

          <div className="mt-4">
            <ArtifactReviewSection
              artifacts={filteredArtifacts}
              canPersist={canPersist}
              pendingKey={pendingKey}
              timeZoneId={timeZoneId}
              onReview={(artifact, outcome) =>
                void handleArtifactReview(artifact, outcome)
              }
            />
          </div>
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

      <div className="workspace-scroll-region shrink-0 pr-1 xl:max-h-[22rem]">
        <ApplicationOverridesSection
          applications={filteredApplications}
          statusOptions={review.statusOptions}
          pendingKey={pendingKey}
          canPersist={canPersist}
          timeZoneId={timeZoneId}
          onSubmit={(event, application) =>
            void handleApplicationSubmit(event, application)
          }
        />
      </div>
    </div>
  );
}

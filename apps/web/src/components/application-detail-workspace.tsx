"use client";

import Link from "next/link";
import { useState } from "react";
import { ApplicationPipelineStepper } from "@/components/application-pipeline-stepper";
import {
  AgentTraceSummaryCard,
  AgentTraceStepsPanel,
  ApplicationThreadEvidenceSection,
} from "@/components/application-thread-evidence-section";
import { InlineMemoryProposals } from "@/components/inline-memory-proposals";
import { ResumeFitPanel } from "@/components/resume-fit-panel";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import {
  formatTimestampInTimeZone,
  toUtcIsoStringFromDateTimeLocal,
} from "@/lib/date-time";
import type {
  ApplicationDetail,
  ApplicationDetailReminder,
  ApplicationThreadMessage,
} from "@/lib/application-detail";
import { formatEnumLabel } from "@/lib/labels";

type ApplicationDetailWorkspaceProps = {
  detail: ApplicationDetail | null;
  source: "api" | "fallback";
  proxyPrefix: string | null;
  currentUserEmail: string;
  currentUserName?: string | null;
  timeZoneId: string;
};

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "No timestamp");
}


function toCompactStageLabel(value: string | null) {
  switch (value) {
    case "application_received":
      return "Received";
    case "recruiter_outreach":
      return "Outreach";
    case "oa_invitation":
      return "OA";
    case "interview_invitation":
      return "Interview";
    case "interview_confirmation":
      return "Confirmed";
    case "generic_update":
      return "Update";
    default:
      return formatEnumLabel(value);
  }
}

function formatConfidence(value: number | null) {
  if (value === null) return "n/a";
  return `${Math.round(value * 100)}%`;
}

function isTerminalStatus(status: string) {
  return status === "Rejected" || status === "Closed";
}

function getStatusBadgeClass(status: string) {
  if (status === "Rejected") {
    return "border-[var(--red-strong)] bg-[var(--red)] text-white shadow-[0_8px_18px_rgba(37,99,235,0.18)]";
  }

  if (status === "Closed") {
    return "bg-[var(--surface-overlay)] text-[var(--text-tertiary)]";
  }

  return "bg-[var(--accent-soft)] text-[var(--accent)]";
}

const APPLICATION_STATUS_OPTIONS = [
  "Applied",
  "Recruiter Contact",
  "Oa Pending",
  "Interviewing",
  "Offer",
  "Rejected",
  "Closed",
];
const maxVisibleRecentActivity = 8;
const maxVisibleArtifactEvidence = 6;

function isAssessmentReminder(action: ApplicationDetailReminder) {
  return (
    action.source === "Assessment Deadline" ||
    action.title.toLowerCase().includes("assessment")
  );
}

function isOaMessage(message: ApplicationThreadMessage) {
  return message.category === "oa_invitation";
}

function buildReviewAgentSummary(message: ApplicationThreadMessage) {
  if (message.requiresManualReview) {
    return (
      message.reviewReason ||
      "Escalated because extraction or application matching was not trustworthy enough."
    );
  }

  return "Allowed this message to update workflow state automatically.";
}

function buildReviewAgentDecisions(detail: ApplicationDetail) {
  return detail.threads
    .flatMap((thread) =>
      thread.messages.map((message) => ({
        ...message,
        threadTitle: thread.title,
        reviewSummary: buildReviewAgentSummary(message),
      })),
    )
    .filter(
      (message) =>
        message.processingSource ||
        message.reviewReason ||
        message.classificationConfidence !== null ||
        message.matchingConfidence !== null,
    )
    .sort(
      (left, right) =>
        new Date(right.receivedAtUtc).getTime() -
        new Date(left.receivedAtUtc).getTime(),
    )
    .slice(0, 6);
}

function findLatestOutboundReply(detail: ApplicationDetail) {
  return detail.threads
    .flatMap((thread) => thread.messages)
    .filter((message) => message.isOutbound)
    .sort(
      (left, right) =>
        new Date(right.receivedAtUtc).getTime() -
        new Date(left.receivedAtUtc).getTime(),
    )[0] ?? null;
}


export function ApplicationDetailWorkspace({
  detail: initialDetail,
  source,
  proxyPrefix,
  timeZoneId,
}: ApplicationDetailWorkspaceProps) {
  const [detail, setDetail] = useState(initialDetail);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingCorrectionId, setPendingCorrectionId] = useState<string | null>(null);

  async function refreshDetailFromApi() {
    if (!proxyPrefix || !detail) {
      return false;
    }

    const response = await fetchWithInFlightDedup(
      `${proxyPrefix}/api/applications/${detail.id}`,
      {
        cache: "no-store",
      },
    );

    if (!response.ok) {
      return false;
    }

    setDetail((await response.json()) as ApplicationDetail);
    return true;
  }

  async function handleMarkNotOa(messageId?: string) {
    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const targetMessageId =
      messageId ??
      detail.threads
        .flatMap((thread) => thread.messages)
        .find((message) => isOaMessage(message))?.id;

    if (!targetMessageId) {
      setError("No OA evidence message is available to correct.");
      return;
    }

    setPendingCorrectionId(targetMessageId);
    setError(null);
    setNotice(null);

    const requestHeaders = {
      "Content-Type": "application/json",
    };

    try {
      const correctionResponse = await fetch(
        `${proxyPrefix}/api/emails/${targetMessageId}/correction`,
        {
          method: "PATCH",
          headers: requestHeaders,
          body: JSON.stringify({
            category: "generic_update",
            actionRequired: false,
            clearDueDate: true,
            notes: "User correction: this email is not an OA assignment.",
          }),
        },
      );

      if (!correctionResponse.ok) {
        throw new Error(`Email correction failed with ${correctionResponse.status}`);
      }

      const assessmentActions = detail.pendingActions.filter(isAssessmentReminder);
      await Promise.all(
        assessmentActions.map(async (action) => {
          const reminderResponse = await fetch(
            `${proxyPrefix}/api/reminders/${action.id}/status`,
            {
              method: "PATCH",
              headers: requestHeaders,
              body: JSON.stringify({ status: "Dismissed" }),
            },
          );

          if (!reminderResponse.ok) {
            throw new Error(`Reminder update failed with ${reminderResponse.status}`);
          }
        }),
      );

      if (detail.status === "Oa Pending") {
        const statusResponse = await fetch(
          `${proxyPrefix}/api/applications/${detail.id}/status`,
          {
            method: "PATCH",
            headers: requestHeaders,
            body: JSON.stringify({
              status: "Applied",
              priority: detail.priority,
            }),
          },
        );

        if (!statusResponse.ok) {
          throw new Error(`Application status update failed with ${statusResponse.status}`);
        }
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) => {
          if (!current) return current;

          return {
            ...current,
            status: current.status === "Oa Pending" ? "Applied" : current.status,
            pendingActions: current.pendingActions.filter(
              (action) => !isAssessmentReminder(action),
            ),
            threads: current.threads.map((thread) => ({
              ...thread,
              messages: thread.messages.map((message) =>
                message.id === targetMessageId
                  ? {
                      ...message,
                      category: "generic_update",
                      actionRequired: false,
                      dueDateUtc: null,
                    }
                  : message,
              ),
            })),
          };
        });
      }

      setNotice("Marked this evidence as not an OA and cleared the assessment action.");
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to save correction.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  async function handleApplicationStatusSubmit(
    event: React.FormEvent<HTMLFormElement>,
  ) {
    event.preventDefault();

    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const formData = new FormData(event.currentTarget);
    const status = String(formData.get("status") ?? detail.status);
    const priority = Number(formData.get("priority") ?? detail.priority);

    setPendingCorrectionId("application-status");
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/applications/${detail.id}/status`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ status, priority }),
        },
      );

      if (!response.ok) {
        throw new Error(`Application update failed with ${response.status}`);
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) =>
          current
            ? {
                ...current,
                status,
                priority,
                lastActivityAtUtc: new Date().toISOString(),
              }
            : current,
        );
      }

      setNotice("Saved application status.");
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to update application.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  async function handleCorrectEmail(
    event: React.FormEvent<HTMLFormElement>,
    message: ApplicationThreadMessage,
  ) {
    event.preventDefault();

    if (!proxyPrefix || !detail) {
      setError("API proxy is not configured.");
      return;
    }

    const formData = new FormData(event.currentTarget);
    const dueDateValue = String(formData.get("dueDateUtc") ?? "");
    const dueDateUtc = dueDateValue
      ? toUtcIsoStringFromDateTimeLocal(dueDateValue, timeZoneId)
      : undefined;

    if (dueDateValue && !dueDateUtc) {
      setError("Invalid due date.");
      return;
    }

    setPendingCorrectionId(message.id);
    setError(null);
    setNotice(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/emails/${message.id}/correction`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            category: String(formData.get("category") ?? message.category ?? ""),
            jobApplicationId: String(formData.get("jobApplicationId") ?? detail.id),
            company: String(formData.get("company") ?? detail.company).trim(),
            role: String(formData.get("role") ?? detail.role).trim(),
            actionRequired: formData.get("actionRequired") === "on",
            dueDateUtc,
            clearDueDate: !dueDateValue,
            notes: String(formData.get("notes") ?? "").trim(),
          }),
        },
      );

      if (!response.ok) {
        throw new Error(`Email correction failed with ${response.status}`);
      }

      const refreshed = await refreshDetailFromApi();
      if (!refreshed) {
        setDetail((current) => {
          if (!current) return current;

          return {
            ...current,
            threads: current.threads.map((thread) => ({
              ...thread,
              messages: thread.messages.map((threadMessage) =>
                threadMessage.id === message.id
                  ? {
                      ...threadMessage,
                      category: String(
                        formData.get("category") ??
                          threadMessage.category ??
                          "generic_update",
                      ),
                      actionRequired: formData.get("actionRequired") === "on",
                      dueDateUtc: dueDateUtc ?? null,
                      requiresManualReview: false,
                    }
                  : threadMessage,
              ),
            })),
          };
        });
      }

      setNotice(`Saved correction for "${message.subject}".`);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to save evidence correction.",
      );
    } finally {
      setPendingCorrectionId(null);
    }
  }

  if (!detail) {
    return (
      <main className="app-scroll-main">
        <div className="mx-auto flex w-full max-w-[104rem] flex-col gap-4 px-5 py-6">
          <section className="card shrink-0 p-6 sm:p-8">
            <p className="eyebrow">Application detail</p>
            <h1 className="mt-3 text-2xl font-bold text-[var(--text-primary)]">
              Not available
            </h1>
            <p className="mt-2 text-sm text-[var(--text-secondary)]">
              No live record for this route. Open one from the dashboard.
            </p>
            <div className="mt-5 flex flex-wrap gap-2">
              <Link href="/app/applications" className="btn btn-primary">
                Back to applications
              </Link>
            </div>
          </section>
        </div>
      </main>
    );
  }

  const terminal = isTerminalStatus(detail.status);
  const rejected = detail.status === "Rejected";
  const visiblePendingActions = terminal ? [] : detail.pendingActions;
  const latestOutboundReply = findLatestOutboundReply(detail);
  const reviewAgentDecisions = buildReviewAgentDecisions(detail);
  const artifactEvidence = detail.artifactEvidence ?? [];
  const visibleArtifactEvidence = artifactEvidence.slice(0, maxVisibleArtifactEvidence);
  const artifactEvidenceTotalCount =
    detail.artifactEvidenceTotalCount ?? artifactEvidence.length;
  const hiddenArtifactEvidenceCount = Math.max(
    0,
    artifactEvidenceTotalCount - visibleArtifactEvidence.length,
  );
  const visibleRecentActivity = detail.recentActivity.slice(0, maxVisibleRecentActivity);
  const recentActivityTotalCount =
    detail.recentActivityTotalCount ?? detail.recentActivity.length;
  const hiddenRecentActivityCount = Math.max(
    0,
    recentActivityTotalCount - visibleRecentActivity.length,
  );

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-4 px-4 py-5 animate-in sm:px-5 sm:py-6 md:h-full md:overflow-hidden">
        <section className="card app-workspace-panel shrink-0 p-4">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <Link
                  href="/app/applications"
                  className="btn btn-secondary btn-sm"
                >
                  Back to applications
                </Link>
                <p className="eyebrow">Application detail</p>
              </div>
              <h1 className="mt-2 text-xl font-bold text-[var(--text-primary)] sm:text-2xl">
                {detail.company} / {detail.role}
              </h1>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <span className={`badge ${getStatusBadgeClass(detail.status)}`}>
                {detail.status}
              </span>
              <span
                className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]"
                title={`Priority ${detail.priority}`}
              >
                P{detail.priority}
              </span>
              {source === "fallback" ? (
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                  Fallback data
                </span>
              ) : null}
            </div>
          </div>

          <div className="mt-4">
            <ApplicationPipelineStepper
              status={detail.status}
              completedStages={detail.completedStages ?? []}
            />
          </div>

          {notice ? (
            <div className="alert alert-success mt-4">
              {notice}
            </div>
          ) : null}
          {error ? (
            <div className="alert alert-error mt-4">
              {error}
            </div>
          ) : null}

        </section>

        <section className="grid gap-4 md:min-h-0 md:flex-1 md:items-stretch md:overflow-hidden xl:grid-cols-[22rem_minmax(0,1fr)] 2xl:grid-cols-[24rem_minmax(0,1fr)]">
          <div className="workspace-scroll-region min-h-0 space-y-4">
            <section className="card overflow-hidden p-0">
              <div className="p-4">
                <p className="eyebrow">Snapshot</p>
                <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3">
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Source
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {detail.source}
                  </dd>
                </div>
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Last activity
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {formatTimestamp(detail.lastActivityAtUtc, timeZoneId)}
                  </dd>
                </div>
                {terminal ? null : (
                  <div>
                    <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                      Open actions
                    </dt>
                    <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                    {visiblePendingActions.length}
                    </dd>
                  </div>
                )}
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Contacts
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {detail.contacts.length}
                  </dd>
                </div>
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Threads
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {detail.threads.length}
                  </dd>
                </div>
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Artifacts
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {artifactEvidenceTotalCount}
                  </dd>
                </div>
                <div>
                  <dt className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--text-tertiary)]">
                    Created
                  </dt>
                  <dd className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  {formatTimestamp(detail.createdAtUtc, timeZoneId)}
                  </dd>
                </div>
                </dl>
                <form
                  key={`${detail.status}:${detail.priority}:${detail.lastActivityAtUtc}`}
                  className="mt-4 grid gap-3 border-t border-[var(--border)] pt-4"
                  onSubmit={handleApplicationStatusSubmit}
                >
                  <div className="grid grid-cols-[minmax(0,1fr)_5rem] gap-3">
                    <label className="block">
                      <span className="eyebrow mb-1.5 block">Stage</span>
                      <select
                        name="status"
                        defaultValue={detail.status}
                        className="input"
                        disabled={source !== "api" || pendingCorrectionId !== null}
                      >
                        {APPLICATION_STATUS_OPTIONS.map((status) => (
                          <option key={status} value={status}>
                            {formatEnumLabel(status)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="block">
                      <span className="eyebrow mb-1.5 block">Priority</span>
                      <input
                        name="priority"
                        type="number"
                        min={1}
                        max={5}
                        defaultValue={detail.priority}
                        className="input"
                        disabled={source !== "api" || pendingCorrectionId !== null}
                        aria-label="Priority (1 highest, 5 lowest)"
                      />
                    </label>
                  </div>
                  <button
                    type="submit"
                    className="btn btn-secondary btn-sm w-full"
                    disabled={source !== "api" || pendingCorrectionId !== null}
                  >
                    {pendingCorrectionId === "application-status"
                      ? "Saving..."
                      : "Save application"}
                  </button>
                </form>
              </div>

              <div className="border-t border-[var(--border)] p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="eyebrow">{terminal ? "Outcome" : "Pending actions"}</p>
                <span
                  className={`badge ${
                    terminal
                      ? getStatusBadgeClass(detail.status)
                      : "bg-[var(--surface-elevated)] text-[var(--text-tertiary)]"
                  }`}
                >
                  {terminal ? detail.status : visiblePendingActions.length}
                </span>
              </div>

              {terminal ? (
                <div
                  className={`mt-4 rounded-[var(--radius-md)] border p-4 ${
                    rejected
                      ? "border-[var(--red-strong)] bg-[var(--red-soft)]"
                      : "border-[var(--border-active)] bg-[var(--surface-overlay)]"
                  }`}
                >
                  <p
                    className={`text-sm font-semibold ${
                      rejected ? "text-[var(--red-strong)]" : "text-[var(--text-primary)]"
                    }`}
                  >
                    No active follow-up for a {detail.status.toLowerCase()} application.
                  </p>
                  <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
                    Historical contacts and thread evidence stay available below for record keeping.
                  </p>
                </div>
              ) : visiblePendingActions.length === 0 ? (
                latestOutboundReply ? (
                  <div className="mt-4 rounded-[var(--radius-md)] border border-[var(--green)]/20 bg-[var(--green-soft)] p-4">
                    <p className="text-sm font-semibold text-[var(--green)]">
                      Last sent reply is already recorded.
                    </p>
                    <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
                      careeroc detected a Gmail reply on{" "}
                      {formatTimestamp(latestOutboundReply.receivedAtUtc, timeZoneId)}.
                    </p>
                    {latestOutboundReply.gmailUrl ? (
                      <div className="mt-3">
                        <a
                          href={latestOutboundReply.gmailUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="btn btn-secondary btn-sm"
                        >
                          Open sent message in Gmail
                        </a>
                      </div>
                    ) : null}
                  </div>
                ) : (
                  <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                    No open reminders.
                  </p>
                )
              ) : (
                <div className="mt-4 space-y-3">
                  {visiblePendingActions.map((action) => (
                    <article key={action.id} className="card-elevated p-4">
                      <p className="text-sm font-medium text-[var(--text-primary)]">
                        {action.title}
                      </p>
                      <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                        {action.source} · {action.status} ·{" "}
                        {action.hasExplicitDueDate
                          ? `due ${formatTimestamp(action.dueAtUtc, timeZoneId)}`
                          : "unknown due date"}
                      </p>
                      {action.sourceThreadId || action.sourceMessageId ? (
                        <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
                          Evidence {action.sourceThreadId ? `thread ${action.sourceThreadId}` : ""}
                          {action.sourceThreadId && action.sourceMessageId ? " · " : ""}
                          {action.sourceMessageId ? `message ${action.sourceMessageId}` : ""}
                        </p>
                      ) : null}
                      {isAssessmentReminder(action) || action.gmailUrl ? (
                        <div className="mt-3 flex flex-wrap gap-2">
                          {isAssessmentReminder(action) ? (
                            <button
                              type="button"
                              className="btn btn-danger btn-sm"
                              onClick={() => void handleMarkNotOa()}
                              disabled={source !== "api" || pendingCorrectionId !== null}
                            >
                              {pendingCorrectionId ? "Saving..." : "Not an OA"}
                            </button>
                          ) : null}
                          {action.gmailUrl ? (
                            <a
                              href={action.gmailUrl}
                              target="_blank"
                              rel="noreferrer"
                              className="btn btn-secondary btn-sm"
                            >
                              Open Gmail evidence
                            </a>
                          ) : null}
                        </div>
                      ) : null}
                    </article>
                  ))}
                </div>
              )}
              </div>

              <div className="border-t border-[var(--border)] p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="eyebrow">Contacts</p>
                <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                  {detail.contacts.length}
                </span>
              </div>

              {detail.contacts.length === 0 ? (
                <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                  No contacts identified.
                </p>
              ) : (
                <div className="mt-4 space-y-3">
                  {detail.contacts.map((contact, index) => (
                    <article
                      key={`${contact.email ?? contact.name ?? "contact"}:${index}`}
                      className="card-elevated p-4"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-sm font-medium text-[var(--text-primary)]">
                            {contact.name ?? contact.email ?? "Unnamed contact"}
                          </p>
                          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                            {contact.type ?? "Unclassified contact"}
                            {contact.roleHint ? ` · ${contact.roleHint}` : ""}
                          </p>
                        </div>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          {contact.messageCount} message{contact.messageCount === 1 ? "" : "s"}
                        </span>
                      </div>
                      {contact.email ? (
                        <p className="mt-3 break-all text-sm leading-6 text-[var(--text-secondary)]">
                          {contact.email}
                        </p>
                      ) : null}
                      <p className="mt-3 text-xs leading-6 text-[var(--text-tertiary)]">
                        Last seen {formatTimestamp(contact.lastSeenAtUtc, timeZoneId)} · latest
                        subject: {contact.latestSubject}
                      </p>
                    </article>
                  ))}
                </div>
              )}
              </div>

              <div className="border-t border-[var(--border)] p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="eyebrow">Artifact evidence</p>
                <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                  {artifactEvidenceTotalCount}
                </span>
              </div>

              {artifactEvidence.length === 0 ? (
                <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                  No uploaded offer, screenshot, or attachment evidence yet.
                </p>
              ) : (
                <div className="mt-4 space-y-3">
                  {visibleArtifactEvidence.map((artifact) => (
                    <article key={artifact.id} className="card-elevated p-4">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <p className="text-[11px] font-mono uppercase tracking-[0.14em] text-[var(--text-tertiary)]">
                            {formatEnumLabel(artifact.artifactKind)}
                          </p>
                          <h3 className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
                            {artifact.safeTitle}
                          </h3>
                          <p className="mt-1 break-all text-xs text-[var(--text-tertiary)]">
                            {artifact.fileName}
                          </p>
                        </div>
                        <span
                          className={`badge ${
                            artifact.requiresReview
                              ? "bg-[var(--yellow-soft)] text-[var(--yellow)]"
                              : "bg-[var(--green-soft)] text-[var(--green)]"
                          }`}
                        >
                          {artifact.requiresReview ? "Review" : "Accepted"}
                        </span>
                      </div>

                      <div className="mt-3 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-3">
                        <p className="eyebrow">
                          Extracted {formatEnumLabel(artifact.extractedFactType)}
                        </p>
                        <p className="mt-1 text-sm font-medium leading-6 text-[var(--text-primary)]">
                          {artifact.extractedFactText}
                        </p>
                        <p className="mt-2 text-xs leading-5 text-[var(--text-tertiary)]">
                          {artifact.sourceSnippet}
                        </p>
                      </div>

                      <div className="mt-3 flex flex-wrap gap-1.5">
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          confidence {formatConfidence(artifact.confidence)}
                        </span>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          added {formatTimestamp(artifact.createdAtUtc, timeZoneId)}
                        </span>
                      </div>

                      {artifact.reviewReason ? (
                        <p className="mt-3 text-xs leading-5 text-[var(--text-secondary)]">
                          {artifact.reviewReason}
                        </p>
                      ) : null}

                      <div className="mt-3">
                        <AgentTraceSummaryCard
                          summary={artifact.traceSummary}
                          title="Artifact trace"
                          compact
                        />
                      </div>
                      <div className="mt-3">
                        <AgentTraceStepsPanel
                          steps={artifact.agentTrace?.steps ?? []}
                          title="Artifact step trace"
                        />
                      </div>
                    </article>
                  ))}
                  {hiddenArtifactEvidenceCount > 0 ? (
                    <p className="rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-4 py-3 text-xs leading-5 text-[var(--text-tertiary)]">
                      Showing the latest {visibleArtifactEvidence.length} artifacts;{" "}
                      {hiddenArtifactEvidenceCount} older artifact
                      {hiddenArtifactEvidenceCount === 1 ? " is" : "s are"} not shown.
                    </p>
                  ) : null}
                </div>
              )}
              </div>

              <div className="border-t border-[var(--border)] p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="eyebrow">Recent activity</p>
                <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                  {recentActivityTotalCount}
                </span>
              </div>

              {detail.recentActivity.length === 0 ? (
                <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                  No events yet.
                </p>
              ) : (
                <div className="mt-4 space-y-3">
                  {visibleRecentActivity.map((activity) => (
                    <article key={activity.id} className="card-elevated p-4">
                      <div className="flex items-start justify-between gap-2">
                        <p className="text-sm font-medium text-[var(--text-primary)]">
                          {activity.description}
                        </p>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          {formatEnumLabel(activity.category)}
                        </span>
                      </div>
                      <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
                        {formatTimestamp(activity.occurredAtUtc, timeZoneId)}
                      </p>
                    </article>
                  ))}
                  {hiddenRecentActivityCount > 0 ? (
                    <p className="rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-4 py-3 text-xs leading-5 text-[var(--text-tertiary)]">
                      Showing the latest {visibleRecentActivity.length} events;{" "}
                      {hiddenRecentActivityCount} older event
                      {hiddenRecentActivityCount === 1 ? " is" : "s are"} not shown.
                    </p>
                  ) : null}
                </div>
              )}
              </div>

              <div className="border-t border-[var(--border)] p-4">
              <div className="flex items-center justify-between gap-3">
                <p className="eyebrow">Evidence agent</p>
                <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                  {reviewAgentDecisions.length}
                </span>
              </div>

              {reviewAgentDecisions.length === 0 ? (
                <p className="mt-3 text-sm text-[var(--text-tertiary)]">
                  No evidence agent decisions yet.
                </p>
              ) : (
                <div className="mt-4 space-y-3">
                  {reviewAgentDecisions.map((decision) => (
                    <article key={decision.id} className="card-elevated p-4">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-sm font-medium text-[var(--text-primary)]">
                            {decision.subject}
                          </p>
                          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                            Evidence Agent
                            {decision.processingSource
                              ? ` · ${decision.processingSource}`
                              : ""}
                          </p>
                        </div>
                        <span
                          className={`badge ${
                            decision.requiresManualReview
                              ? "bg-[var(--red-soft)] text-[var(--red)]"
                              : "bg-[var(--green-soft)] text-[var(--green)]"
                          }`}
                        >
                          {decision.requiresManualReview ? "Escalated" : "Cleared"}
                        </span>
                      </div>
                      <p className="mt-3 text-sm leading-7 text-[var(--text-secondary)]">
                        {decision.reviewSummary}
                      </p>
                      <div className="mt-3">
                        <AgentTraceSummaryCard
                          summary={decision.traceSummary}
                          title="Decision trace"
                          compact
                        />
                      </div>
                      <div className="mt-3 flex flex-wrap gap-1.5">
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          {toCompactStageLabel(decision.category)}
                        </span>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          class {formatConfidence(decision.classificationConfidence)}
                        </span>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          match {formatConfidence(decision.matchingConfidence)}
                        </span>
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          {formatTimestamp(decision.receivedAtUtc, timeZoneId)}
                        </span>
                      </div>
                      <p className="mt-3 text-xs leading-5 text-[var(--text-tertiary)]">
                        Thread: {decision.threadTitle}
                      </p>
                    </article>
                  ))}
                </div>
              )}
              </div>
            </section>
          </div>

          <div className="workspace-scroll-region min-h-0 space-y-4">
            <section className="card app-workspace-panel p-4">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <p className="eyebrow">Agent brief</p>
                  <h2 className="mt-1.5 text-base font-semibold text-[var(--text-primary)]">
                    {detail.agentBrief.headline}
                  </h2>
                </div>
                <span className="badge w-fit bg-[var(--accent-soft)] text-[var(--accent)]">
                  {detail.agentBrief.recommendedSurface}
                </span>
              </div>
              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                {detail.agentBrief.summary}
              </p>
              <div className="mt-3 rounded-md border border-[var(--border-active)]/40 bg-[var(--surface)] p-3">
                <p className="eyebrow text-[var(--accent)]">Next move</p>
                <p className="mt-1.5 text-sm leading-6 text-[var(--text-primary)]">
                  {detail.agentBrief.nextAction}
                </p>
              </div>
              <div className="mt-3">
                <AgentTraceStepsPanel
                  steps={detail.agentTrace?.steps ?? []}
                  title="Orchestrator step trace"
                />
              </div>
            </section>

            <ResumeFitPanel applicationId={detail.id} proxyPrefix={proxyPrefix} />

            <InlineMemoryProposals
              proposals={detail.memoryProposals}
              proxyPrefix={proxyPrefix}
              title="Application memory"
              emptyCopy="No application-scoped memory proposals are waiting here."
            />

            <ApplicationThreadEvidenceSection
              threads={detail.threads}
              source={source}
              pendingCorrectionId={pendingCorrectionId}
              timeZoneId={timeZoneId}
              applicationId={detail.id}
              applicationCompany={detail.company}
              applicationRole={detail.role}
              onCorrectEmail={handleCorrectEmail}
              onMarkNotOa={(messageId) => void handleMarkNotOa(messageId)}
            />
          </div>
        </section>

      </div>
    </main>
  );
}

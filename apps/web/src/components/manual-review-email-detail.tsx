"use client";

import Link from "next/link";
import { AgentTraceSummaryCard } from "@/components/application-thread-evidence-section";
import {
  formatDateTimeLocalInputInTimeZone,
  formatTimestampInTimeZone,
} from "@/lib/date-time";
import type {
  ReviewApplication,
  ReviewAgentTraceStep,
  ReviewEmail,
} from "@/lib/manual-review";
import { formatEnumLabel } from "@/lib/labels";

type ManualReviewEmailDetailProps = {
  email: ReviewEmail | null;
  applications: ReviewApplication[];
  categoryOptions: string[];
  timeZoneId: string;
  canPersist: boolean;
  pendingKey: string | null;
  onSubmit: (
    event: React.FormEvent<HTMLFormElement>,
    email: ReviewEmail,
  ) => void;
};

function formatTimestamp(value: string, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "");
}

function toLocalDateTimeInputValue(value: string | null, timeZoneId: string) {
  return formatDateTimeLocalInputInTimeZone(value, timeZoneId);
}


function formatConfidence(value: number | null) {
  if (value === null) return "n/a";
  return value.toFixed(2);
}

function toStageLabel(value: string) {
  return value
    .split("_")
    .map((part) => part[0]?.toUpperCase() + part.slice(1))
    .join(" ");
}

function buildReviewReason(email: ReviewEmail) {
  return (
    email.reviewReason ??
    "careeroc could not confidently classify or assign this message, so it is waiting for a human decision."
  );
}

function findAssignedApplication(
  email: ReviewEmail,
  applications: ReviewApplication[],
) {
  if (!email.jobApplicationId) {
    return null;
  }

  return (
    applications.find((application) => application.id === email.jobApplicationId) ??
    null
  );
}

function AgentTraceStepCard({ step }: { step: ReviewAgentTraceStep }) {
  return (
    <article className="card-elevated p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="eyebrow">{toStageLabel(step.stage)}</p>
          <h5 className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
            {step.agentName}
          </h5>
        </div>
        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {toStageLabel(step.stage)}
        </span>
      </div>

      <p className="mt-3 text-sm leading-7 text-[var(--text-primary)]">
        {step.summary}
      </p>

      {step.reason ? (
        <p className="mt-2 text-sm leading-7 text-[var(--text-secondary)]">
          {step.reason}
        </p>
      ) : null}

      {step.facts.length > 0 ? (
        <div className="mt-4 flex flex-wrap gap-2">
          {step.facts.map((fact) => (
            <div
              key={`${step.stage}:${fact.label}`}
              className="rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--surface)] px-3 py-2"
            >
              <p className="text-[10px] uppercase tracking-[0.22em] text-[var(--text-tertiary)]">
                {fact.label}
              </p>
              <p className="mt-1 text-xs font-medium text-[var(--text-primary)]">
                {fact.value}
              </p>
            </div>
          ))}
        </div>
      ) : null}
    </article>
  );
}

export function ManualReviewEmailDetail({
  email,
  applications,
  categoryOptions,
  timeZoneId,
  canPersist,
  pendingKey,
  onSubmit,
}: ManualReviewEmailDetailProps) {
  if (!email) {
    return (
      <section className="card flex items-center justify-center p-8">
        <p className="text-sm text-[var(--text-tertiary)]">
          Pick an item from the queue.
        </p>
      </section>
    );
  }

  const assignedApplication = findAssignedApplication(email, applications);
  const confidencePct =
    email.classificationConfidence === null
      ? null
      : Math.min(email.classificationConfidence * 100, 100);
  const confidenceColor =
    confidencePct === null
      ? "var(--text-tertiary)"
      : confidencePct >= 70
        ? "var(--green)"
        : confidencePct >= 40
          ? "var(--yellow)"
          : "var(--red)";

  const metaCells: Array<{
    label: string;
    value: React.ReactNode;
    accent?: string;
    extra?: React.ReactNode;
  }> = [
    {
      label: "Category",
      value: formatEnumLabel(email.category),
    },
    {
      label: "Assigned",
      value: assignedApplication
        ? `${assignedApplication.company} / ${assignedApplication.role}`
        : "Not assigned",
      extra: assignedApplication ? (
        <Link
          href={`/app/applications/${assignedApplication.id}`}
          className="mt-1 inline-flex text-[11px] font-medium text-[var(--accent)] hover:text-[var(--accent-hover)]"
        >
          Open detail →
        </Link>
      ) : null,
    },
    {
      label: "Contact",
      value: email.contactName ?? email.contactEmail ?? "—",
      extra: email.contactType ? (
        <p className="mt-0.5 text-[11px] text-[var(--text-tertiary)]">
          {email.contactType}
        </p>
      ) : null,
    },
    {
      label: "Company",
      value: email.extractedCompany ?? "—",
    },
    {
      label: "Role",
      value: email.extractedRole ?? "—",
    },
    {
      label: "Confidence",
      value:
        confidencePct === null ? (
          <span className="text-[var(--text-tertiary)]">n/a</span>
        ) : (
          <span style={{ color: confidenceColor }}>
            {formatConfidence(email.classificationConfidence)}
          </span>
        ),
      extra:
        confidencePct === null ? null : (
          <div className="confidence-track mt-2">
            <div
              className="confidence-fill"
              style={{ width: `${confidencePct}%`, background: confidenceColor }}
            />
          </div>
        ),
    },
  ];

  return (
    <section className="card p-0">
      <header className="flex flex-col gap-2 border-b border-[var(--border)] px-5 py-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <p className="text-[11px] font-mono text-[var(--text-tertiary)]">
            {email.sender}
          </p>
          <h3 className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
            {email.subject}
          </h3>
          <p className="mt-1 text-sm leading-6 text-[var(--text-secondary)]">
            {email.snippet}
          </p>
        </div>
        <span className="badge shrink-0 bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {formatTimestamp(email.receivedAtUtc, timeZoneId)}
        </span>
      </header>

      <div className="flex flex-col gap-4 px-5 py-4">
        <div className="rounded-[var(--radius-md)] border border-[var(--red)]/25 bg-[var(--red-soft)]/50 px-4 py-3">
          <p className="eyebrow text-[var(--red)]">Why this stopped automation</p>
          <p className="mt-2 text-sm leading-7 text-[var(--text-primary)]">
            {buildReviewReason(email)}
          </p>
        </div>

        <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {metaCells.map((cell) => (
            <div
              key={cell.label}
              className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2.5"
            >
              <p className="eyebrow">{cell.label}</p>
              <p className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                {cell.value}
              </p>
              {cell.extra}
            </div>
          ))}
        </div>

        <details className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2.5">
          <summary className="flex cursor-pointer items-center justify-between gap-3 list-none">
            <p className="eyebrow">Body</p>
            <span className="text-xs text-[var(--text-tertiary)]">Expand</span>
          </summary>
          <div className="mt-3 rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--surface)] px-4 py-3 text-sm leading-7 text-[var(--text-secondary)]">
            {email.bodyPreview}
          </div>
        </details>

        <AgentTraceSummaryCard
          summary={email.traceSummary ?? email.agentTrace?.summary ?? null}
          title="Structured model trace"
        />

        {email.threadMessages.length > 1 ? (
          <details className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2.5">
            <summary className="flex cursor-pointer items-center justify-between gap-3 list-none">
              <p className="eyebrow">Thread timeline</p>
              <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                {email.threadMessages.length}
              </span>
            </summary>
            <div className="mt-3 divide-y divide-[var(--border)] overflow-hidden rounded-[var(--radius-sm)] border border-[var(--border)]">
              {email.threadMessages.map((message) => (
                <article
                  key={message.id}
                  className={`px-4 py-3 ${
                    message.isCurrentEmail
                      ? "bg-[var(--surface)] shadow-[inset_3px_0_0_var(--accent)]"
                      : "bg-[rgba(255,255,255,0.42)]"
                  }`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-[11px] font-mono text-[var(--text-tertiary)]">
                        {message.sender}
                      </p>
                      <p className="mt-0.5 text-sm font-medium text-[var(--text-primary)]">
                        {message.subject}
                      </p>
                    </div>
                    {message.isCurrentEmail ? (
                      <span className="badge bg-[var(--accent-soft)] text-[var(--accent)]">
                        Current
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-2 text-xs leading-5 text-[var(--text-secondary)]">
                    {message.snippet}
                  </p>
                  <p className="mt-1 text-[10px] text-[var(--text-tertiary)]">
                    {formatTimestamp(message.receivedAtUtc, timeZoneId)}
                  </p>
                </article>
              ))}
            </div>
          </details>
        ) : null}

        {email.agentTrace?.steps.length ? (
          <details className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2.5">
            <summary className="flex cursor-pointer items-center justify-between gap-3 list-none">
              <p className="eyebrow">Agent trace</p>
              <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                {email.agentTrace.steps.length} steps
              </span>
            </summary>
            <div className="mt-3 grid gap-2 xl:grid-cols-2">
              {email.agentTrace.steps.map((step) => (
                <AgentTraceStepCard key={`${email.id}:${step.stage}`} step={step} />
              ))}
            </div>
          </details>
        ) : null}
      </div>

      <form
        className="border-t border-[var(--border)] bg-[rgba(255,255,255,0.32)] px-5 py-4"
        onSubmit={(event) => onSubmit(event, email)}
      >
        <p className="eyebrow text-[var(--accent)]">Apply correction</p>

        <div className="mt-3 space-y-3">
          <div className="grid gap-3 lg:grid-cols-2">
            <label className="block">
              <span className="eyebrow block mb-1.5">Category</span>
              <select
                name="category"
                defaultValue={email.category ?? categoryOptions[0]}
                className="input"
                disabled={!canPersist || pendingKey !== null}
              >
                {categoryOptions.map((category) => (
                  <option key={category} value={category}>
                    {formatEnumLabel(category)}
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="eyebrow block mb-1.5">Application</span>
              <select
                name="jobApplicationId"
                defaultValue={email.jobApplicationId ?? "__auto__"}
                className="input"
                disabled={!canPersist || pendingKey !== null}
              >
                {!email.jobApplicationId ? (
                  <option value="__auto__">Auto match or create</option>
                ) : null}
                <option value="__none__">
                  {email.jobApplicationId ? "Remove assignment" : "Keep unassigned"}
                </option>
                {applications.map((application) => (
                  <option key={application.id} value={application.id}>
                    {application.company} / {application.role}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="grid gap-3 lg:grid-cols-2">
            <label className="block">
              <span className="eyebrow block mb-1.5">Company</span>
              <input
                name="company"
                defaultValue={email.extractedCompany ?? ""}
                className="input"
                disabled={!canPersist || pendingKey !== null}
              />
            </label>
            <label className="block">
              <span className="eyebrow block mb-1.5">Role</span>
              <input
                name="role"
                defaultValue={email.extractedRole ?? ""}
                className="input"
                disabled={!canPersist || pendingKey !== null}
              />
            </label>
          </div>

          <div className="grid gap-3 lg:grid-cols-[1fr_auto] lg:items-end">
            <label className="block">
              <span className="eyebrow block mb-1.5">Due date</span>
              <input
                name="dueDateUtc"
                type="datetime-local"
                defaultValue={toLocalDateTimeInputValue(email.dueDateUtc, timeZoneId)}
                className="input"
                disabled={!canPersist || pendingKey !== null}
              />
            </label>
            <label className="flex items-center gap-2 rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2.5 text-sm text-[var(--text-primary)]">
              <input
                name="actionRequired"
                type="checkbox"
                defaultChecked={email.actionRequired}
                disabled={!canPersist || pendingKey !== null}
                className="h-4 w-4 accent-[var(--accent)]"
              />
              Action required
            </label>
          </div>

          <label className="block">
            <span className="eyebrow block mb-1.5">Note (optional)</span>
            <textarea
              name="notes"
              rows={3}
              defaultValue={email.notes ?? ""}
              className="input"
              disabled={!canPersist || pendingKey !== null}
              placeholder="Why this correction — saved for later reviews."
            />
          </label>

          <button
            type="submit"
            className="btn btn-primary btn-block"
            disabled={!canPersist || pendingKey !== null}
          >
            {pendingKey === `email:${email.id}`
              ? <><span className="spinner" /> Saving...</>
              : "Apply correction"}
          </button>
        </div>
      </form>
    </section>
  );
}

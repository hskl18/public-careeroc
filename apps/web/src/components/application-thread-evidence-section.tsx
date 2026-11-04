"use client";

import {
  formatDateTimeLocalInputInTimeZone,
  formatTimestampInTimeZone,
} from "@/lib/date-time";
import type {
  AgentTraceStep,
  ApplicationThread,
  ApplicationThreadMessage,
} from "@/lib/application-detail";
import { formatEnumLabel } from "@/lib/labels";

const EMAIL_CATEGORY_OPTIONS = [
  "recruiter_outreach",
  "application_received",
  "oa_invitation",
  "interview_invitation",
  "interview_confirmation",
  "rejection",
  "offer",
  "generic_update",
];
const maxVisibleThreads = 8;
const maxVisibleMessagesPerThread = 5;

type AgentTraceSummaryCardSummary = {
  modelPath: string | null;
  purpose: string;
  confidence: number | null;
  evidenceSource: string;
  reviewGateResult: string;
  fallbackPath: string;
};

type AgentTraceSummaryCardProps = {
  summary: AgentTraceSummaryCardSummary | null;
  title?: string;
  compact?: boolean;
};

function formatConfidenceValue(value: number | null) {
  if (value === null) {
    return "n/a";
  }

  return value.toFixed(2);
}

export function AgentTraceSummaryCard({
  summary,
  title = "Model trace summary",
  compact = false,
}: AgentTraceSummaryCardProps) {
  if (!summary) {
    return null;
  }

  const rows = [
    ["Model path", summary.modelPath ?? "not recorded"],
    ["Purpose", summary.purpose],
    ["Confidence", formatConfidenceValue(summary.confidence)],
    ["Evidence source", summary.evidenceSource],
    ["Review gate", summary.reviewGateResult],
    ["Fallback", summary.fallbackPath],
  ];

  return (
    <div className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[rgba(255,255,255,0.46)] p-3">
      <div className="flex items-center justify-between gap-3">
        <p className="eyebrow text-[var(--accent)]">{title}</p>
        {summary.modelPath ? (
          <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
            {summary.modelPath}
          </span>
        ) : null}
      </div>
      <div
        className={`mt-3 grid gap-2 ${
          compact ? "sm:grid-cols-2" : "sm:grid-cols-2 xl:grid-cols-3"
        }`}
      >
        {rows.map(([label, value]) => (
          <div
            key={label}
            className="rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--surface)] px-3 py-2"
          >
            <p className="text-[10px] uppercase tracking-[0.22em] text-[var(--text-tertiary)]">
              {label}
            </p>
            <p className="mt-1 text-xs font-medium leading-5 text-[var(--text-primary)]">
              {value}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}

function toStageLabel(value: string) {
  return value
    .split("_")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function AgentTraceStepCard({ step }: { step: AgentTraceStep }) {
  return (
    <article className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--surface)] p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="eyebrow">{toStageLabel(step.stage)}</p>
          <h5 className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
            {step.agentName}
          </h5>
        </div>
        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {toStageLabel(step.stage)}
        </span>
      </div>

      <p className="mt-3 text-sm leading-6 text-[var(--text-primary)]">
        {step.summary}
      </p>

      {step.reason ? (
        <p className="mt-2 text-xs leading-5 text-[var(--text-secondary)]">
          {step.reason}
        </p>
      ) : null}

      {step.facts.length > 0 ? (
        <div className="mt-3 flex flex-wrap gap-2">
          {step.facts.map((fact) => (
            <div
              key={`${step.stage}:${fact.label}`}
              className="rounded-[var(--radius-sm)] border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2"
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

export function AgentTraceStepsPanel({
  steps,
  title = "Step trace",
}: {
  steps: AgentTraceStep[];
  title?: string;
}) {
  if (steps.length === 0) {
    return null;
  }

  return (
    <details className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
      <summary className="flex cursor-pointer items-center justify-between gap-3 text-xs font-semibold text-[var(--text-secondary)]">
        <span>{title}</span>
        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {steps.length} steps
        </span>
      </summary>
      <div className="mt-3 grid gap-2">
        {steps.map((step) => (
          <AgentTraceStepCard key={`${step.stage}:${step.agentName}`} step={step} />
        ))}
      </div>
    </details>
  );
}

type ThreadEvidenceSectionProps = {
  threads: ApplicationThread[];
  source: "api" | "fallback";
  pendingCorrectionId: string | null;
  timeZoneId: string;
  applicationId: string;
  applicationCompany: string;
  applicationRole: string;
  onCorrectEmail: (
    event: React.FormEvent<HTMLFormElement>,
    message: ApplicationThreadMessage,
  ) => void;
  onMarkNotOa: (messageId: string) => void;
};

export function ApplicationThreadEvidenceSection({
  threads,
  source,
  pendingCorrectionId,
  timeZoneId,
  applicationId,
  applicationCompany,
  applicationRole,
  onCorrectEmail,
  onMarkNotOa,
}: ThreadEvidenceSectionProps) {
  const totalMessages = threads.reduce(
    (total, thread) => total + thread.messageCount,
    0,
  );
  const visibleThreads = threads.slice(0, maxVisibleThreads);
  const hiddenThreadCount = Math.max(0, threads.length - visibleThreads.length);

  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="eyebrow">Source evidence</p>
          <h2 className="mt-1.5 text-base font-semibold text-[var(--text-primary)]">
            Recruiting threads
          </h2>
          <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
            {totalMessages} source message{totalMessages === 1 ? "" : "s"} across{" "}
            {threads.length} recruiting thread{threads.length === 1 ? "" : "s"}.
          </p>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          {threads.length}
        </span>
      </div>

      {threads.length === 0 ? (
        <p className="mt-5 text-sm text-[var(--text-tertiary)]">
          No linked emails yet.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          {visibleThreads.map((thread) => (
            <ThreadEvidenceBlock
              key={thread.threadId}
              thread={thread}
              source={source}
              pendingCorrectionId={pendingCorrectionId}
              timeZoneId={timeZoneId}
              applicationId={applicationId}
              applicationCompany={applicationCompany}
              applicationRole={applicationRole}
              onCorrectEmail={onCorrectEmail}
              onMarkNotOa={onMarkNotOa}
            />
          ))}
          {hiddenThreadCount > 0 ? (
            <p className="rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-4 py-3 text-xs leading-5 text-[var(--text-tertiary)]">
              Showing the latest {visibleThreads.length} threads; {hiddenThreadCount} older
              thread{hiddenThreadCount === 1 ? " is" : "s are"} not shown.
            </p>
          ) : null}
        </div>
      )}
    </section>
  );
}

function ThreadEvidenceBlock({
  thread,
  source,
  pendingCorrectionId,
  timeZoneId,
  applicationId,
  applicationCompany,
  applicationRole,
  onCorrectEmail,
  onMarkNotOa,
}: Omit<ThreadEvidenceSectionProps, "threads"> & { thread: ApplicationThread }) {
  const visibleMessages = thread.messages.slice(0, maxVisibleMessagesPerThread);
  const hiddenMessageCount = Math.max(0, thread.messages.length - visibleMessages.length);

  return (
    <article className="overflow-hidden rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.44)]">
      <div className="border-b border-[var(--border)] p-4">
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_auto]">
          <div className="min-w-0">
            <p className="eyebrow">Thread</p>
            <h3 className="mt-2 text-base font-semibold leading-6 text-[var(--text-primary)]">
              {thread.title}
            </h3>
            <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
              {thread.snippet}
            </p>
          </div>

          <div className="flex flex-col gap-2 xl:items-end">
            <div className="flex flex-wrap items-center gap-1.5 xl:justify-end">
              {thread.stageLabel ? (
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                  {toCompactStageLabel(thread.stageLabel)}
                </span>
              ) : null}
              {thread.actionRequired ? (
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                  Action
                </span>
              ) : null}
              {thread.requiresManualReview ? (
                <span className="badge bg-[var(--red-soft)] text-[var(--red)]">
                  Agent check
                </span>
              ) : null}
              {thread.hasOutboundReply ? (
                <span className="badge bg-[var(--green-soft)] text-[var(--green)]">
                  Replied
                </span>
              ) : null}
              <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                {thread.messageCount} message{thread.messageCount === 1 ? "" : "s"}
              </span>
            </div>
            <p className="text-[11px] text-[var(--text-tertiary)]">
              Latest {formatTimestamp(thread.lastMessageAtUtc, timeZoneId)}
            </p>
            {thread.gmailUrl ? (
              <a
                href={thread.gmailUrl}
                target="_blank"
                rel="noreferrer"
                className="btn btn-secondary btn-sm"
              >
                Open thread
              </a>
            ) : null}
          </div>
        </div>
      </div>

      <div className="space-y-3 p-4">
        {visibleMessages.map((message) => (
          <MessageEvidenceItem
            key={message.id}
            message={message}
            source={source}
            pendingCorrectionId={pendingCorrectionId}
            timeZoneId={timeZoneId}
            applicationId={applicationId}
            applicationCompany={applicationCompany}
            applicationRole={applicationRole}
            onCorrectEmail={onCorrectEmail}
            onMarkNotOa={onMarkNotOa}
          />
        ))}
        {hiddenMessageCount > 0 ? (
          <p className="rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-4 py-3 text-xs leading-5 text-[var(--text-tertiary)]">
            Showing the latest {visibleMessages.length} messages in this thread;{" "}
            {hiddenMessageCount} older message{hiddenMessageCount === 1 ? " is" : "s are"} not
            shown.
          </p>
        ) : null}
      </div>
    </article>
  );
}

function MessageEvidenceItem({
  message,
  source,
  pendingCorrectionId,
  timeZoneId,
  applicationId,
  applicationCompany,
  applicationRole,
  onCorrectEmail,
  onMarkNotOa,
}: Omit<ThreadEvidenceSectionProps, "threads"> & {
  message: ApplicationThreadMessage;
}) {
  const isOa = isOaMessage(message);
  const hasMessageActions = isOa || message.gmailUrl !== null;
  const canPersist = source === "api";

  return (
    <article className="overflow-hidden rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.64)]">
      <div className="border-b border-[var(--border)] px-4 py-3">
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                {message.isOutbound ? "Sent" : "Received"}
              </span>
              {message.category ? (
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                  {toCompactStageLabel(message.category)}
                </span>
              ) : null}
              {message.actionRequired ? (
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                  Action
                </span>
              ) : null}
              {message.requiresManualReview ? (
                <span className="badge bg-[var(--red-soft)] text-[var(--red)]">
                  Agent check
                </span>
              ) : null}
              {message.hasFeedbackNote ? (
                <span className="badge bg-[var(--accent-soft)] text-[var(--accent)]">
                  Note
                </span>
              ) : null}
            </div>
            <p className="mt-3 break-all text-[11px] font-mono text-[var(--text-tertiary)]">
              {message.sender}
            </p>
            <h4 className="mt-1 text-sm font-semibold leading-6 text-[var(--text-primary)]">
              {message.subject}
            </h4>
          </div>

          {hasMessageActions ? (
            <div className="flex shrink-0 flex-wrap gap-2 md:justify-end">
              {isOa ? (
                <button
                  type="button"
                  className="btn btn-danger btn-sm"
                  onClick={() => onMarkNotOa(message.id)}
                  disabled={source !== "api" || pendingCorrectionId !== null}
                >
                  {pendingCorrectionId === message.id ? "Saving..." : "Not an OA"}
                </button>
              ) : null}
              {message.gmailUrl ? (
                <a
                  href={message.gmailUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="btn btn-secondary btn-sm"
                >
                  Open Gmail
                </a>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>

      <div className="grid gap-4 p-4 xl:grid-cols-[minmax(0,1fr)_15rem]">
        <div className="min-w-0 space-y-3">
          <div className="rounded-md border border-[var(--border-active)]/35 bg-[var(--surface)] p-4">
            <p className="eyebrow text-[var(--accent)]">Agent read</p>
            <p className="mt-2 text-sm leading-7 text-[var(--text-primary)]">
              {message.summary}
            </p>
          </div>

          <details className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)]">
            <summary className="px-4 py-3 text-xs font-semibold text-[var(--text-secondary)]">
              Email excerpt
            </summary>
            <p className="border-t border-[var(--border)] px-4 py-3 text-sm leading-7 text-[var(--text-secondary)]">
              {message.bodyPreview}
            </p>
          </details>

          <details className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)]">
            <summary className="px-4 py-3 text-xs font-semibold text-[var(--text-secondary)]">
              Correct evidence
            </summary>
            <form
              key={[
                message.id,
                message.category,
                message.actionRequired,
                message.dueDateUtc,
                message.requiresManualReview,
              ].join(":")}
              className="grid gap-3 border-t border-[var(--border)] p-4"
              onSubmit={(event) => onCorrectEmail(event, message)}
            >
              <div className="grid gap-3 md:grid-cols-2">
                <label className="block">
                  <span className="eyebrow mb-1.5 block">Category</span>
                  <select
                    name="category"
                    defaultValue={message.category ?? "generic_update"}
                    className="input"
                    disabled={!canPersist || pendingCorrectionId !== null}
                  >
                    {EMAIL_CATEGORY_OPTIONS.map((category) => (
                      <option key={category} value={category}>
                        {formatEnumLabel(category)}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="block">
                  <span className="eyebrow mb-1.5 block">Due date</span>
                  <input
                    name="dueDateUtc"
                    type="datetime-local"
                    defaultValue={formatDateTimeLocalInputInTimeZone(
                      message.dueDateUtc,
                      timeZoneId,
                    )}
                    className="input"
                    disabled={!canPersist || pendingCorrectionId !== null}
                  />
                </label>
              </div>

              <div className="grid gap-3 md:grid-cols-2">
                <label className="block">
                  <span className="eyebrow mb-1.5 block">Company</span>
                  <input
                    name="company"
                    defaultValue={applicationCompany}
                    className="input"
                    disabled={!canPersist || pendingCorrectionId !== null}
                  />
                </label>
                <label className="block">
                  <span className="eyebrow mb-1.5 block">Role</span>
                  <input
                    name="role"
                    defaultValue={applicationRole}
                    className="input"
                    disabled={!canPersist || pendingCorrectionId !== null}
                  />
                </label>
              </div>

              <input type="hidden" name="jobApplicationId" value={applicationId} />

              <div className="grid gap-3 md:grid-cols-[1fr_auto] md:items-end">
                <label className="block">
                  <span className="eyebrow mb-1.5 block">Note</span>
                  <input
                    name="notes"
                    defaultValue={message.reviewReason ?? ""}
                    className="input"
                    disabled={!canPersist || pendingCorrectionId !== null}
                    placeholder="Correction note"
                  />
                </label>
                <label className="flex min-h-11 items-center gap-2 rounded-md border border-[var(--border)] bg-white/55 px-3 text-sm text-[var(--text-primary)]">
                  <input
                    name="actionRequired"
                    type="checkbox"
                    defaultChecked={message.actionRequired}
                    disabled={!canPersist || pendingCorrectionId !== null}
                    className="h-4 w-4 accent-[var(--accent)]"
                  />
                  Action required
                </label>
              </div>

              <button
                type="submit"
                className="btn btn-secondary btn-sm w-full sm:w-fit"
                disabled={!canPersist || pendingCorrectionId !== null}
              >
                {pendingCorrectionId === message.id ? "Saving..." : "Save evidence correction"}
              </button>
            </form>
          </details>
        </div>

        <aside className="space-y-3">
          <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
            <p className="eyebrow">{message.isOutbound ? "Sent" : "Received"}</p>
            <p className="mt-1 text-sm font-medium leading-5 text-[var(--text-primary)]">
              {formatTimestamp(message.receivedAtUtc, timeZoneId)}
            </p>
          </div>

          <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
            <p className="eyebrow">Confidence</p>
            <div className="mt-2 grid grid-cols-2 gap-2">
              <div>
                <p className="text-[10px] uppercase tracking-[0.22em] text-[var(--text-tertiary)]">
                  Class
                </p>
                <p className="mt-1 font-mono text-xs font-semibold text-[var(--text-primary)]">
                  {formatConfidence(message.classificationConfidence)}
                </p>
              </div>
              <div>
                <p className="text-[10px] uppercase tracking-[0.22em] text-[var(--text-tertiary)]">
                  Match
                </p>
                <p className="mt-1 font-mono text-xs font-semibold text-[var(--text-primary)]">
                  {formatConfidence(message.matchingConfidence)}
                </p>
              </div>
            </div>
          </div>

          {message.contactName || message.contactEmail ? (
            <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
              <p className="eyebrow">Contact</p>
              <p className="mt-2 text-sm font-medium text-[var(--text-primary)]">
                {message.contactName ?? message.contactEmail}
              </p>
              <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                {message.contactType ?? "Unclassified contact"}
                {message.contactRoleHint ? ` · ${message.contactRoleHint}` : ""}
              </p>
              {message.contactEmail ? (
                <p className="mt-2 break-all text-xs text-[var(--text-secondary)]">
                  {message.contactEmail}
                </p>
              ) : null}
            </div>
          ) : null}

          {message.dueDateUtc ? (
            <div className="rounded-md border border-[var(--yellow)]/25 bg-[var(--yellow-soft)] p-3">
              <p className="eyebrow text-[var(--yellow)]">Due date</p>
              <p className="mt-2 text-sm font-medium text-[var(--text-primary)]">
                {formatTimestamp(message.dueDateUtc, timeZoneId)}
              </p>
            </div>
          ) : null}

          {message.reviewReason || message.processingSource ? (
            <details className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
              <summary className="text-xs font-semibold text-[var(--text-secondary)]">
                Extraction
              </summary>
              <div className="mt-3 space-y-2 text-xs leading-5 text-[var(--text-tertiary)]">
                {message.reviewReason ? (
                  <p>
                    <span className="font-medium text-[var(--text-primary)]">
                      Reason:
                    </span>{" "}
                    {message.reviewReason}
                  </p>
                ) : null}
                {message.processingSource ? (
                  <p>
                    <span className="font-medium text-[var(--text-primary)]">
                      Source:
                    </span>{" "}
                    {message.processingSource}
                  </p>
                ) : null}
              </div>
            </details>
          ) : null}

          <AgentTraceSummaryCard
            summary={message.traceSummary}
            title="Trace"
            compact
          />

          <AgentTraceStepsPanel steps={message.agentTrace?.steps ?? []} />
        </aside>
      </div>
    </article>
  );
}

function isOaMessage(message: ApplicationThreadMessage) {
  return message.category === "oa_invitation";
}

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

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
import { formatEnumLabel, formatConfidence } from "@/lib/labels";

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
  summary?: AgentTraceSummaryCardSummary | null;
  title?: string;
  compact?: boolean;
};

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
    ["Confidence", formatConfidence(summary.confidence)],
    ["Evidence source", summary.evidenceSource],
    ["Review gate", summary.reviewGateResult],
    ["Fallback", summary.fallbackPath],
  ];

  return (
    <div className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--surface-strong)] p-3">
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
            <p className="text-xs text-[var(--text-tertiary)]">
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
              <p className="text-xs text-[var(--text-tertiary)]">
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
    <details className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-3">
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
  const visibleThreads = threads.slice(0, maxVisibleThreads);
  const hiddenThreadCount = Math.max(0, threads.length - visibleThreads.length);

  return (
    <section className="card p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-base font-semibold text-[var(--text-primary)]">
            Threads
          </h2>
        </div>
        <span className="text-xs tabular-nums text-[var(--text-tertiary)]">{threads.length}</span>
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
            <p className="px-1 text-xs text-[var(--text-tertiary)]">+{hiddenThreadCount} older</p>
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
    <article className="overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface-strong)]">
      <div className="border-b border-[var(--border)] p-4">
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_auto]">
          <div className="min-w-0">
            <h3 className="text-base font-semibold leading-6 text-[var(--text-primary)]">
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
            <p className="text-xs text-[var(--text-tertiary)]">
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
          <p className="px-1 text-xs text-[var(--text-tertiary)]">+{hiddenMessageCount} older</p>
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
    <article className="overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface-strong)]">
      <div className="border-b border-[var(--border)] px-4 py-3">
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
                {message.isOutbound ? "Sent" : "Received"}
              </span>
              {message.category &&
              toCompactStageLabel(message.category) !== (message.isOutbound ? "Sent" : "Received") ? (
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
            <p className="mt-3 break-all text-xs text-[var(--text-tertiary)]">
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
          <p className="text-sm leading-6 text-[var(--text-primary)]">{message.summary}</p>

          <details className="border-t border-[var(--border)] pt-2">
            <summary className="text-xs font-semibold text-[var(--text-secondary)]">
              Email excerpt
            </summary>
            <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
              {message.bodyPreview}
            </p>
          </details>

          <details className="border-t border-[var(--border)] pt-2">
            <summary className="text-xs font-semibold text-[var(--text-secondary)]">
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
              className="mt-2 grid gap-3"
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
                <label className="flex min-h-11 items-center gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-3 text-sm text-[var(--text-primary)]">
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
          {/* Flat label and value rows; the message is already a card. */}
          <dl className="divide-y divide-[var(--border)] text-xs">
            {[
              [message.isOutbound ? "Sent" : "Received", formatTimestamp(message.receivedAtUtc, timeZoneId)],
              ["Type", formatConfidence(message.classificationConfidence)],
              ["Match", formatConfidence(message.matchingConfidence)],
              ...(message.dueDateUtc ? [["Due", formatTimestamp(message.dueDateUtc, timeZoneId)]] : []),
              ...(message.contactName || message.contactEmail
                ? [["Contact", message.contactName ?? message.contactEmail ?? ""]]
                : []),
            ].map(([label, value]) => (
              <div key={label} className="flex items-baseline justify-between gap-3 py-1.5">
                <dt className="text-[var(--text-tertiary)]">{label}</dt>
                <dd className="min-w-0 break-all text-right font-medium text-[var(--text-primary)]">{value}</dd>
              </div>
            ))}
          </dl>

          {message.reviewReason || message.processingSource ? (
            <details className="border-t border-[var(--border)] pt-2">
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


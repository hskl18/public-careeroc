"use client";

import type { FormEvent, ReactNode } from "react";
import { AgentTraceSummaryCard } from "@/components/application-thread-evidence-section";
import { StatusPill } from "@/components/status-pill";
import type { ApplicationDetail } from "@/lib/application-detail";
import {
  APPLICATION_STATUS_OPTIONS,
  buildReviewAgentDecisions,
  findLatestOutboundReply,
  isAssessmentReminder,
  isTerminalStatus,
  toCompactStageLabel,
} from "@/lib/application-detail-view";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import { formatConfidence, formatEnumLabel } from "@/lib/labels";

const maxVisibleRecentActivity = 8;

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "No timestamp");
}

function SidebarSection({
  title,
  count,
  children,
}: {
  title: string;
  count: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="border-t border-[var(--border)] p-4">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">{title}</h2>
        {count}
      </div>
      {children}
    </div>
  );
}

function CountBadge({ value }: { value: number }) {
  return (
    <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">{value}</span>
  );
}

function CountText({ value }: { value: number }) {
  return <span className="text-xs tabular-nums text-[var(--text-tertiary)]">{value}</span>;
}

function EmptyLine({ children }: { children: ReactNode }) {
  return <p className="mt-3 text-sm text-[var(--text-tertiary)]">{children}</p>;
}

function GmailLink({ href }: { href: string }) {
  return (
    <a href={href} target="_blank" rel="noreferrer" className="btn btn-secondary btn-sm">
      Gmail
    </a>
  );
}

function StatusForm({
  detail,
  disabled,
  saving,
  onSubmit,
}: {
  detail: ApplicationDetail;
  disabled: boolean;
  saving: boolean;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  return (
    <div className="p-4">
      <form
        key={`${detail.status}:${detail.priority}:${detail.lastActivityAtUtc}`}
        className="grid gap-3"
        onSubmit={onSubmit}
      >
        <div className="grid grid-cols-[minmax(0,1fr)_5rem] gap-3">
          <label className="block">
            <span className="eyebrow mb-1.5 block">Stage</span>
            <select name="status" defaultValue={detail.status} className="input" disabled={disabled}>
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
              disabled={disabled}
              aria-label="Priority (1 highest, 5 lowest)"
            />
          </label>
        </div>
        <button type="submit" className="btn btn-secondary btn-sm w-full" disabled={disabled}>
          {saving ? "Saving..." : "Save application"}
        </button>
      </form>
    </div>
  );
}

function ToDoSection({
  detail,
  timeZoneId,
  disabled,
  saving,
  onMarkNotOa,
}: {
  detail: ApplicationDetail;
  timeZoneId: string;
  disabled: boolean;
  saving: boolean;
  onMarkNotOa: () => void;
}) {
  const terminal = isTerminalStatus(detail.status);
  const rejected = detail.status === "Rejected";
  const actions = terminal ? [] : detail.pendingActions;
  const latestOutboundReply = findLatestOutboundReply(detail);

  return (
    <SidebarSection
      title={terminal ? "Outcome" : "To do"}
      count={terminal ? <StatusPill status={detail.status} /> : <CountBadge value={actions.length} />}
    >
      {terminal ? (
        <div
          className={`mt-4 rounded-[var(--radius-md)] border p-4 ${
            rejected
              ? "border-[var(--red-strong)] bg-[var(--red-soft)]"
              : "border-[var(--border-active)] bg-[var(--surface-overlay)]"
          }`}
        >
          <p className={`text-sm font-semibold ${rejected ? "text-[var(--red-strong)]" : "text-[var(--text-primary)]"}`}>
            No active follow-up for a {detail.status.toLowerCase()} application.
          </p>
        </div>
      ) : actions.length === 0 ? (
        latestOutboundReply ? (
          <div className="mt-4 rounded-[var(--radius-md)] border border-[var(--green)]/20 bg-[var(--green-soft)] p-4">
            <p className="text-sm font-semibold text-[var(--green)]">Reply sent</p>
            <p className="text-xs text-[var(--text-secondary)]">
              {formatTimestamp(latestOutboundReply.receivedAtUtc, timeZoneId)}
            </p>
            {latestOutboundReply.gmailUrl ? (
              <div className="mt-3">
                <GmailLink href={latestOutboundReply.gmailUrl} />
              </div>
            ) : null}
          </div>
        ) : (
          <EmptyLine>No open reminders.</EmptyLine>
        )
      ) : (
        <div className="mt-1 divide-y divide-[var(--border)]">
          {actions.map((action) => (
            <article key={action.id} className="py-2">
              <p className="text-sm font-medium text-[var(--text-primary)]">{action.title}</p>
              {action.hasExplicitDueDate ? (
                <p className="text-xs text-[var(--accent)]">
                  Due {formatTimestamp(action.dueAtUtc, timeZoneId)}
                </p>
              ) : null}
              {isAssessmentReminder(action) || action.gmailUrl ? (
                <div className="mt-2 flex flex-wrap gap-2">
                  {isAssessmentReminder(action) ? (
                    <button type="button" className="btn btn-danger btn-sm" onClick={onMarkNotOa} disabled={disabled}>
                      {saving ? "Saving..." : "Not an OA"}
                    </button>
                  ) : null}
                  {action.gmailUrl ? <GmailLink href={action.gmailUrl} /> : null}
                </div>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </SidebarSection>
  );
}

function ContactsSection({ detail, timeZoneId }: { detail: ApplicationDetail; timeZoneId: string }) {
  return (
    <SidebarSection title="Contacts" count={<CountText value={detail.contacts.length} />}>
      {detail.contacts.length === 0 ? (
        <EmptyLine>No contacts identified.</EmptyLine>
      ) : (
        <ul className="mt-1 divide-y divide-[var(--border)]">
          {detail.contacts.map((contact, index) => (
            <li key={`${contact.email ?? contact.name ?? "contact"}:${index}`} className="py-2">
              <p className="truncate text-sm text-[var(--text-primary)]">
                {contact.name ?? contact.email ?? "Unnamed contact"}
              </p>
              <p className="truncate text-xs text-[var(--text-tertiary)]">
                {contact.messageCount} message{contact.messageCount === 1 ? "" : "s"} ·{" "}
                {formatTimestamp(contact.lastSeenAtUtc, timeZoneId)}
              </p>
            </li>
          ))}
        </ul>
      )}
    </SidebarSection>
  );
}

function RecentActivitySection({ detail, timeZoneId }: { detail: ApplicationDetail; timeZoneId: string }) {
  const visible = detail.recentActivity.slice(0, maxVisibleRecentActivity);
  const totalCount = detail.recentActivityTotalCount ?? detail.recentActivity.length;
  const hiddenCount = Math.max(0, totalCount - visible.length);

  return (
    <SidebarSection title="Recent activity" count={<CountBadge value={totalCount} />}>
      {detail.recentActivity.length === 0 ? (
        <EmptyLine>No events yet.</EmptyLine>
      ) : (
        <div className="mt-4 space-y-3">
          {visible.map((activity) => (
            <article key={activity.id} className="card-elevated p-4">
              <div className="flex items-start justify-between gap-2">
                <p className="text-sm font-medium text-[var(--text-primary)]">{activity.description}</p>
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                  {formatEnumLabel(activity.category)}
                </span>
              </div>
              <p className="mt-2 text-xs text-[var(--text-tertiary)]">
                {formatTimestamp(activity.occurredAtUtc, timeZoneId)}
              </p>
            </article>
          ))}
          {hiddenCount > 0 ? (
            <p className="px-1 text-xs text-[var(--text-tertiary)]">+{hiddenCount} older</p>
          ) : null}
        </div>
      )}
    </SidebarSection>
  );
}

function EvidenceAgentSection({ detail, timeZoneId }: { detail: ApplicationDetail; timeZoneId: string }) {
  const decisions = buildReviewAgentDecisions(detail);

  return (
    <SidebarSection title="Evidence agent" count={<CountText value={decisions.length} />}>
      {decisions.length === 0 ? (
        <EmptyLine>No evidence agent decisions yet.</EmptyLine>
      ) : (
        <div className="mt-4 space-y-3">
          {decisions.map((decision) => (
            <article key={decision.id} className="card-elevated p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-[var(--text-primary)]">{decision.subject}</p>
                  <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                    Evidence Agent
                    {decision.processingSource ? ` · ${decision.processingSource}` : ""}
                  </p>
                </div>
                <span
                  className={`badge ${
                    decision.requiresManualReview
                      ? "bg-[var(--red-soft)] text-[var(--red)]"
                      : "bg-[var(--green-soft)] text-[var(--green)]"
                  }`}
                >
                  {decision.requiresManualReview ? "Email review" : "Cleared"}
                </span>
              </div>
              <p className="mt-3 text-sm leading-7 text-[var(--text-secondary)]">{decision.reviewSummary}</p>
              <div className="mt-3">
                <AgentTraceSummaryCard summary={decision.traceSummary} title="Decision trace" compact />
              </div>
              <div className="mt-3 flex flex-wrap gap-1.5">
                {[
                  toCompactStageLabel(decision.category),
                  `Type ${formatConfidence(decision.classificationConfidence)}`,
                  `Match ${formatConfidence(decision.matchingConfidence)}`,
                  formatTimestamp(decision.receivedAtUtc, timeZoneId),
                ].map((label, index) => (
                  <span key={index} className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                    {label}
                  </span>
                ))}
              </div>
              <p className="mt-3 text-xs leading-5 text-[var(--text-tertiary)]">
                Thread: {decision.threadTitle}
              </p>
            </article>
          ))}
        </div>
      )}
    </SidebarSection>
  );
}

// The left column of application detail: the stage form, then to-do, contacts, activity, and the
// evidence agent's decisions.
export function ApplicationDetailSidebar({
  detail,
  timeZoneId,
  disabled,
  pendingCorrectionId,
  onStatusSubmit,
  onMarkNotOa,
}: {
  detail: ApplicationDetail;
  timeZoneId: string;
  disabled: boolean;
  pendingCorrectionId: string | null;
  onStatusSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onMarkNotOa: () => void;
}) {
  return (
    <section className="card overflow-hidden p-0">
      <StatusForm
        detail={detail}
        disabled={disabled}
        saving={pendingCorrectionId === "application-status"}
        onSubmit={onStatusSubmit}
      />
      <ToDoSection
        detail={detail}
        timeZoneId={timeZoneId}
        disabled={disabled}
        saving={pendingCorrectionId !== null}
        onMarkNotOa={onMarkNotOa}
      />
      <ContactsSection detail={detail} timeZoneId={timeZoneId} />
      <RecentActivitySection detail={detail} timeZoneId={timeZoneId} />
      <EvidenceAgentSection detail={detail} timeZoneId={timeZoneId} />
    </section>
  );
}

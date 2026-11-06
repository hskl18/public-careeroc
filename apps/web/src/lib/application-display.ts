import type { ApplicationCard, PendingAction } from "@/lib/dashboard";

// Shared by the application list and its evidence panel, so a row and its panel never disagree.

export type DerivedNextAction = {
  text: string;
  due: string | null;
  tone: string;
  reminder?: PendingAction;
};

export function deriveNextAction(
  application: ApplicationCard,
  reminders: PendingAction[],
): DerivedNextAction {
  return {
    text: application.nextAction.title,
    due: application.nextAction.dueAtUtc,
    tone: application.nextAction.tone,
    reminder: reminders.find(
      (reminder) => reminder.id === application.nextAction.sourceReminderId,
    ),
  };
}

export function describePriority(application: ApplicationCard) {
  const label = application.priorityLabel;
  return {
    label,
    toneClass: priorityToneClass(label),
    reason: application.priorityReason,
  };
}

function priorityToneClass(label: string) {
  switch (label.toLowerCase()) {
    case "urgent":
      return "bg-[var(--red-soft)] text-[var(--red-strong)]";
    case "high":
      return "bg-[var(--yellow-soft)] text-[var(--yellow)]";
    case "medium":
      return "bg-[var(--blue-soft)] text-[var(--blue)]";
    case "low":
      return "bg-[var(--surface-overlay)] text-[var(--text-secondary)]";
    default:
      return "bg-[var(--surface-overlay)] text-[var(--text-tertiary)]";
  }
}

export function nextActionToneClass(tone: DerivedNextAction["tone"]) {
  switch (tone) {
    case "action":
      return "text-[var(--accent)]";
    case "review":
      return "text-[var(--yellow)]";
    case "waiting":
      return "text-[var(--green)]";
    default:
      return "text-[var(--text-secondary)]";
  }
}

export type DueDescription = {
  relative: string | null;
  pill: { label: string; className: string } | null;
};

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

// The row's "tomorrow" and its "Due today" pill come from one count of calendar days in the
// viewer's time, so they can never disagree.
export function describeDue(value: string | null, now = new Date()): DueDescription {
  const target = value ? new Date(value) : null;
  if (!target || Number.isNaN(target.getTime())) {
    return { relative: null, pill: null };
  }

  const days = Math.round((startOfDay(target) - startOfDay(now)) / 86_400_000);
  const relative =
    days === 0
      ? "today"
      : days === 1
        ? "tomorrow"
        : days === -1
          ? "yesterday"
          : days > 0 && days <= 14
            ? `in ${days}d`
            : days < 0 && days >= -30
              ? `${Math.abs(days)}d ago`
              : null;
  const pill =
    target.getTime() < now.getTime()
      ? { label: "Overdue", className: "bg-[var(--red-soft)] text-[var(--red)]" }
      : days === 0
        ? { label: "Due today", className: "bg-[var(--red-soft)] text-[var(--red)]" }
        : days <= 3
          ? { label: "Due soon", className: "bg-[var(--yellow-soft)] text-[var(--yellow)]" }
          : null;

  return { relative, pill };
}


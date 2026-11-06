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

// The calendar day of a moment in the profile's time zone, as a day number, so "today" means the
// same day as the timestamps shown beside it.
function dayNumber(date: Date, timeZoneId: string) {
  const [year, month, day] = new Intl.DateTimeFormat("en-CA", {
    timeZone: timeZoneId,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  })
    .format(date)
    .split("-")
    .map(Number);
  return Date.UTC(year, month - 1, day) / 86_400_000;
}

// The row's "tomorrow" and its "Due today" pill come from one count of calendar days, so they can
// never disagree.
export function describeDue(
  value: string | null,
  timeZoneId: string,
  now = new Date(),
): DueDescription {
  const target = value ? new Date(value) : null;
  if (!target || Number.isNaN(target.getTime())) {
    return { relative: null, pill: null };
  }

  const days = dayNumber(target, timeZoneId) - dayNumber(now, timeZoneId);
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


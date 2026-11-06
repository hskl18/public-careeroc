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

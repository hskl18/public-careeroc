import type {
  ApplicationCard,
  ApplicationNextAction,
  DashboardResponse,
  PendingAction,
} from "./dashboard";

export type SortColumn = "company" | "role" | "stage" | "next-action" | "priority";
export type SortDirection = "asc" | "desc";
export type SortState = { column: SortColumn | null; direction: SortDirection };

const STATUS_SORT_RANK: Record<string, number> = {
  Offer: 70,
  Interviewing: 60,
  "Recruiter Contact": 50,
  "Oa Pending": 45,
  Applied: 35,
  Rejected: 10,
  Closed: 0,
};

const NEXT_ACTION_TONE_RANK: Record<string, number> = {
  action: 40,
  review: 30,
  waiting: 20,
  muted: 10,
};

function compareText(left: string, right: string, direction: SortDirection) {
  const result = left.localeCompare(right, undefined, {
    numeric: true,
    sensitivity: "base",
  });

  return direction === "asc" ? result : -result;
}

function compareNullableTime(
  left: string | null,
  right: string | null,
  direction: SortDirection,
) {
  const leftMs = left ? new Date(left).getTime() : Number.POSITIVE_INFINITY;
  const rightMs = right ? new Date(right).getTime() : Number.POSITIVE_INFINITY;
  const normalizedLeft = Number.isNaN(leftMs) ? Number.POSITIVE_INFINITY : leftMs;
  const normalizedRight = Number.isNaN(rightMs) ? Number.POSITIVE_INFINITY : rightMs;

  return direction === "asc"
    ? normalizedRight - normalizedLeft
    : normalizedLeft - normalizedRight;
}

function byRecentActivity(left: ApplicationCard, right: ApplicationCard) {
  return (
    new Date(right.lastActivityAtUtc).getTime() -
    new Date(left.lastActivityAtUtc).getTime()
  );
}

function compareByColumn(
  left: ApplicationCard,
  right: ApplicationCard,
  column: SortColumn,
  direction: SortDirection,
) {
  let result = 0;

  switch (column) {
    case "company":
      result = compareText(left.company, right.company, direction);
      break;
    case "role":
      result = compareText(left.role, right.role, direction);
      break;
    case "stage":
      result =
        (STATUS_SORT_RANK[right.status] ?? 20) -
        (STATUS_SORT_RANK[left.status] ?? 20);
      if (direction === "asc") result *= -1;
      break;
    case "next-action": {
      result =
        (NEXT_ACTION_TONE_RANK[right.nextAction.tone] ?? 0) -
        (NEXT_ACTION_TONE_RANK[left.nextAction.tone] ?? 0);
      if (result === 0) {
        result = compareNullableTime(
          left.nextAction.dueAtUtc,
          right.nextAction.dueAtUtc,
          "desc",
        );
      }
      if (result === 0) {
        result = compareText(left.nextAction.title, right.nextAction.title, "asc");
      }
      if (direction === "asc") result *= -1;
      break;
    }
    case "priority":
      result = left.priority - right.priority;
      if (direction === "asc") result *= -1;
      break;
  }

  return result !== 0 ? result : byRecentActivity(left, right);
}

function getDueSoonState(value: string, nowMs: number) {
  const target = new Date(value).getTime();
  if (Number.isNaN(target)) return null;

  const diffHours = (target - nowMs) / (1000 * 60 * 60);
  if (diffHours < 0) return "overdue";
  if (diffHours <= 24) return "today";
  if (diffHours <= 48) return "tomorrow";
  if (diffHours <= 72) return "soon";
  return null;
}

// The API sends urgencyScore; this local score covers a card that lacks one.
export function getApplicationUrgencyScore(application: ApplicationCard, nowMs = Date.now()) {
  if (typeof application.urgencyScore === "number") {
    return application.urgencyScore;
  }

  let score = 0;
  const dueAtUtc = application.nextActionDueAtUtc ?? application.nextAction.dueAtUtc;
  const dueSoon = dueAtUtc ? getDueSoonState(dueAtUtc, nowMs) : null;

  if (dueSoon === "overdue" || dueSoon === "today" || dueSoon === "tomorrow") {
    score += 100;
  } else if (dueSoon === "soon") {
    score += 60;
  }

  if (application.actionRequired) score += 40;
  if (application.requiresReview) score += 30;
  if (["Oa Pending", "Interviewing", "Offer"].includes(application.status)) {
    score += 20;
  }
  if (["Rejected", "Closed"].includes(application.status)) {
    score -= 10;
  }

  return score;
}

// A chosen column sorts by that column; otherwise action required first, then urgency, priority,
// and recent activity.
export function sortApplications(applications: ApplicationCard[], sort: SortState) {
  return [...applications].sort((left, right) => {
    if (sort.column) {
      return compareByColumn(left, right, sort.column, sort.direction);
    }

    if (left.actionRequired !== right.actionRequired) {
      return left.actionRequired ? -1 : 1;
    }
    const urgencyDelta =
      getApplicationUrgencyScore(right) - getApplicationUrgencyScore(left);
    if (urgencyDelta !== 0) {
      return urgencyDelta;
    }
    if (left.priority !== right.priority) {
      return left.priority - right.priority;
    }
    return byRecentActivity(left, right);
  });
}

function nextActionFor(title: string, tone: ApplicationNextAction["tone"]): ApplicationNextAction {
  return { title, dueAtUtc: null, hasExplicitDueDate: false, tone, sourceReminderId: null };
}

function fallbackNextActionForApplication(application: ApplicationCard): ApplicationNextAction {
  switch (application.status) {
    case "Oa Pending":
      return nextActionFor("Complete assessment", "action");
    case "Interviewing":
      return nextActionFor("Prepare interview", "action");
    case "Offer":
      return nextActionFor("Reply to offer", "action");
    case "Recruiter Contact":
      return nextActionFor("Reply to recruiter", "action");
    case "Applied":
      return application.hasOutboundReply
        ? nextActionFor("Wait for response", "waiting")
        : nextActionFor("Confirm application sent", "muted");
    case "Rejected":
    case "Closed":
      return nextActionFor("Archive or dispute", "muted");
    default:
      return nextActionFor("Review application", "muted");
  }
}

function nextActionFromReminder(reminder: PendingAction): ApplicationNextAction {
  return {
    title: reminder.title,
    dueAtUtc: reminder.hasExplicitDueDate ? reminder.dueAtUtc : null,
    hasExplicitDueDate: reminder.hasExplicitDueDate,
    tone: "action",
    sourceReminderId: reminder.id,
  };
}

// What the dashboard shows after a task is marked done, until the refreshed one arrives: the
// reminder is gone, an assessment stage moves back to Applied, and the next action is recomputed.
export function completeApplicationActionLocally(
  dashboard: DashboardResponse,
  applicationId: string,
  reminderId?: string,
  now = new Date().toISOString(),
): DashboardResponse {
  const pendingActions = reminderId
    ? dashboard.pendingActions.filter((action) => action.id !== reminderId)
    : dashboard.pendingActions;
  let completedWasActionRequired = false;
  let completedStillRequiresAction = false;
  const applications = dashboard.applications.map((application) => {
    if (application.id !== applicationId) {
      return application;
    }

    const status = application.status === "Oa Pending" ? "Applied" : application.status;
    const remainingReminder = pendingActions.find(
      (action) => action.jobApplicationId === application.id,
    );
    completedWasActionRequired = application.actionRequired;
    completedStillRequiresAction = Boolean(remainingReminder);
    const updatedApplication = {
      ...application,
      status,
      actionRequired: Boolean(remainingReminder),
      lastActivityAtUtc: now,
    };

    return {
      ...updatedApplication,
      nextAction: remainingReminder
        ? nextActionFromReminder(remainingReminder)
        : fallbackNextActionForApplication(updatedApplication),
    };
  });

  return {
    ...dashboard,
    metrics: {
      ...dashboard.metrics,
      needsAction:
        completedWasActionRequired && !completedStillRequiresAction
          ? Math.max(0, dashboard.metrics.needsAction - 1)
          : dashboard.metrics.needsAction,
    },
    applications,
    pendingActions,
  };
}

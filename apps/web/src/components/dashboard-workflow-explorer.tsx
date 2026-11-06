"use client";

import type { Route } from "next";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  DashboardApplicationEvidencePanel,
  DashboardEmptySelectedPanel,
} from "@/components/dashboard-application-evidence-panel";
import {
  DashboardApplicationList,
  type DashboardSortColumn,
  type DashboardSortDirection,
} from "@/components/dashboard-application-list";
import { WorkspaceMetricTile } from "@/components/workspace-controls";
import type {
  ApplicationCard,
  DashboardResponse,
  DashboardView,
  PendingAction,
} from "@/lib/dashboard";
import { readErrorMessage } from "@/lib/client-fetch";

type WorkflowExplorerProps = {
  dashboard: DashboardResponse;
  timeZoneId: string;
  // The view the server loaded; the list below holds only that view's applications.
  view: DashboardView;
};

type SavedView = DashboardView;

type SortColumn = DashboardSortColumn;

type SortDirection = DashboardSortDirection;

const STATUS_SORT_RANK: Record<string, number> = {
  Offer: 70,
  Interviewing: 60,
  "Recruiter Contact": 50,
  "Oa Pending": 45,
  Applied: 35,
  Rejected: 10,
  Closed: 0,
};

const NEXT_ACTION_TONE_RANK: Record<
  ApplicationCard["nextAction"]["tone"],
  number
> = {
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

function compareApplications(
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
        NEXT_ACTION_TONE_RANK[right.nextAction.tone] -
        NEXT_ACTION_TONE_RANK[left.nextAction.tone];
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

  if (result !== 0) {
    return result;
  }

  return (
    new Date(right.lastActivityAtUtc).getTime() -
    new Date(left.lastActivityAtUtc).getTime()
  );
}

function getApplicationDueAt(application: ApplicationCard) {
  return application.nextActionDueAtUtc ?? application.nextAction.dueAtUtc;
}

function getDueSoonState(value: string) {
  const target = new Date(value).getTime();
  if (Number.isNaN(target)) return null;

  const diffHours = (target - Date.now()) / (1000 * 60 * 60);
  if (diffHours < 0) return "overdue";
  if (diffHours <= 24) return "today";
  if (diffHours <= 48) return "tomorrow";
  if (diffHours <= 72) return "soon";
  return null;
}

function getApplicationUrgencyScore(application: ApplicationCard) {
  if (typeof application.urgencyScore === "number") {
    return application.urgencyScore;
  }

  let score = 0;
  const dueAtUtc = getApplicationDueAt(application);
  const dueSoon = dueAtUtc ? getDueSoonState(dueAtUtc) : null;

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

const SAVED_VIEW_LABEL: Record<SavedView, string> = {
  "needs-action": "Needs action",
  "needs-review": "Fix",
  active: "Active",
  waiting: "Waiting",
  interviewing: "Interviews",
  offer: "Offers",
  rejected: "Rejected",
  all: "All",
};

function fallbackNextActionForApplication(application: ApplicationCard) {
  switch (application.status) {
    case "Oa Pending":
      return {
        title: "Complete assessment",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "action" as const,
        sourceReminderId: null,
      };
    case "Interviewing":
      return {
        title: "Prepare interview",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "action" as const,
        sourceReminderId: null,
      };
    case "Offer":
      return {
        title: "Reply to offer",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "action" as const,
        sourceReminderId: null,
      };
    case "Recruiter Contact":
      return {
        title: "Reply to recruiter",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "action" as const,
        sourceReminderId: null,
      };
    case "Applied":
      return application.hasOutboundReply
        ? {
            title: "Wait for response",
            dueAtUtc: null,
            hasExplicitDueDate: false,
            tone: "waiting" as const,
            sourceReminderId: null,
          }
        : {
            title: "Confirm application sent",
            dueAtUtc: null,
            hasExplicitDueDate: false,
            tone: "muted" as const,
            sourceReminderId: null,
          };
    case "Rejected":
    case "Closed":
      return {
        title: "Archive or dispute",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "muted" as const,
        sourceReminderId: null,
      };
    default:
      return {
        title: "Review application",
        dueAtUtc: null,
        hasExplicitDueDate: false,
        tone: "muted" as const,
        sourceReminderId: null,
      };
  }
}

function nextActionFromReminder(reminder: PendingAction) {
  return {
    title: reminder.title,
    dueAtUtc: reminder.hasExplicitDueDate ? reminder.dueAtUtc : null,
    hasExplicitDueDate: reminder.hasExplicitDueDate,
    tone: "action" as const,
    sourceReminderId: reminder.id,
  };
}

function completeApplicationActionLocally(
  dashboard: DashboardResponse,
  applicationId: string,
  reminderId?: string,
) {
  const now = new Date().toISOString();
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

export function DashboardWorkflowExplorer({
  dashboard,
  timeZoneId,
  view,
}: WorkflowExplorerProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [completedActions, setCompletedActions] = useState<
    Array<{ applicationId: string; reminderId?: string }>
  >([]);
  const [pendingCompletionKey, setPendingCompletionKey] = useState<string | null>(
    null,
  );
  const [actionNotice, setActionNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const currentDashboard = useMemo(
    () =>
      completedActions.reduce(
        (current, completed) =>
          completeApplicationActionLocally(
            current,
            completed.applicationId,
            completed.reminderId,
          ),
        dashboard,
      ),
    [completedActions, dashboard],
  );
  const initialQuery = useMemo(
    () => searchParams.get("q")?.trim() ?? "",
    [searchParams],
  );
  const [savedView, setSavedView] = useState<SavedView>(view);
  const [pipelineSearch, setPipelineSearch] = useState(initialQuery);
  const [selectedApplicationId, setSelectedApplicationId] = useState<
    string | null
  >(null);
  const [sortState, setSortState] = useState<{
    column: SortColumn | null;
    direction: SortDirection;
  }>({ column: null, direction: "desc" });
  const applicationListRef = useRef<HTMLDivElement | null>(null);
  const normalizedPipelineSearch = pipelineSearch.trim();
  const searchChanged = normalizedPipelineSearch !== initialQuery;

  const sortedApplications = useMemo(() => {
    return [...currentDashboard.applications].sort((left, right) => {
      if (sortState.column) {
        return compareApplications(
          left,
          right,
          sortState.column,
          sortState.direction,
        );
      }

      // Action Required first, then backend urgency when available, local
      // due-date urgency, priority, and recent activity.
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
      return (
        new Date(right.lastActivityAtUtc).getTime() -
        new Date(left.lastActivityAtUtc).getTime()
      );
    });
  }, [currentDashboard.applications, sortState.column, sortState.direction]);

  const activeApplication =
    sortedApplications.find(
      (application) => application.id === selectedApplicationId,
    ) ??
    sortedApplications[0] ??
    null;

  const needsReviewCount = currentDashboard.metrics.needsReview;
  const reviewQueueCount = currentDashboard.metrics.reviewQueue ?? null;

  const kpiCards: Array<{
    label: string;
    view: SavedView;
    value: number | null;
    accent?: string;
    highlight?: boolean;
  }> = [
    {
      label: "Needs action",
      view: "needs-action",
      value: currentDashboard.metrics.needsAction,
      accent: "var(--accent)",
      highlight: true,
    },
    {
      // Applications whose company or role look misread; not the email review queue.
      label: "Fix",
      view: "needs-review",
      value: needsReviewCount,
      accent: "var(--brand-blue-ink)",
    },
    {
      label: "Active",
      view: "active",
      value: currentDashboard.metrics.activeApplications,
      accent: "var(--blue)",
    },
    {
      label: "Waiting",
      view: "waiting",
      value: currentDashboard.metrics.waitingOnEmployer,
      accent: "var(--green)",
    },
    {
      label: "Interviews",
      view: "interviewing",
      value: currentDashboard.metrics.interviews,
      accent: "var(--blue)",
    },
    {
      label: "Offers",
      view: "offer",
      value: currentDashboard.metrics.offers,
      accent: "var(--green)",
    },
    {
      label: "Rejected",
      view: "rejected",
      value: currentDashboard.metrics.rejections,
      accent: "var(--red)",
    },
    {
      label: "All",
      view: "all",
      value: currentDashboard.metrics.totalApplications,
      accent: "var(--text-tertiary)",
    },
  ];

  const totalApplications = currentDashboard.metrics.totalApplications;
  function selectView(next: SavedView, nextQuery = pipelineSearch) {
    setSavedView(next);
    applicationListRef.current?.scrollTo({ top: 0, left: 0 });
    const params = new URLSearchParams(searchParams.toString());
    params.delete("focus");
    if (next === "needs-action") {
      params.delete("view");
    } else {
      params.set("view", next);
    }

    const trimmedQuery = nextQuery.trim();
    if (trimmedQuery) {
      params.set("q", trimmedQuery);
    } else {
      params.delete("q");
    }

    router.replace(
      `/app/applications${params.size > 0 ? `?${params.toString()}` : ""}` as Route,
    );
  }

  function selectSort(column: SortColumn) {
    setSortState((current) => {
      if (current.column === column) {
        return {
          column,
          direction: current.direction === "desc" ? "asc" : "desc",
        };
      }

      return { column, direction: "desc" };
    });
    applicationListRef.current?.scrollTo({ top: 0, left: 0 });
  }

  function openApplicationFromRow(applicationId: string) {
    if (window.matchMedia("(max-width: 1279px)").matches) {
      router.push(`/app/applications/${applicationId}` as Route);
      return;
    }

    setSelectedApplicationId(applicationId);
  }

  async function completeApplicationAction(
    application: ApplicationCard,
    reminder?: PendingAction,
  ) {
    const shouldMoveOaToApplied = application.status === "Oa Pending";
    if (!reminder && !shouldMoveOaToApplied) {
      setActionNotice("No open task is attached to this action.");
      return;
    }

    const requestKey = `${application.id}:${reminder?.id ?? "stage"}:Completed`;
    setPendingCompletionKey(requestKey);
    setActionError(null);
    setActionNotice(null);

    try {
      if (reminder) {
        const reminderResponse = await fetch(
          `/api/careeroc/api/reminders/${reminder.id}/status`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ status: "Completed" }),
          },
        );

        if (!reminderResponse.ok) {
          throw new Error(await readErrorMessage(reminderResponse));
        }
      }

      if (shouldMoveOaToApplied) {
        const statusResponse = await fetch(
          `/api/careeroc/api/applications/${application.id}/status`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              status: "Applied",
              priority: application.priority,
            }),
          },
        );

        if (!statusResponse.ok) {
          throw new Error(await readErrorMessage(statusResponse));
        }
      }

      setCompletedActions((current) => [
        ...current,
        { applicationId: application.id, reminderId: reminder?.id },
      ]);
      setActionNotice(`Marked done: ${application.company}.`);
      router.refresh();
    } catch (requestError) {
      // The reminder may be done even if the stage update failed; reload what the API has.
      router.refresh();
      setActionError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to mark this task done.",
      );
    } finally {
      setPendingCompletionKey(null);
    }
  }

  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      applicationListRef.current?.scrollTo({ top: 0, left: 0 });
    });

    return () => window.cancelAnimationFrame(frame);
  }, [savedView, initialQuery, sortState.column, sortState.direction]);

  return (
    <section
      className="grid min-h-0 flex-1 grid-cols-1 items-stretch gap-4 overflow-hidden xl:grid-cols-[minmax(0,1fr)_26rem]"
    >
      <div className="flex min-h-0 flex-col gap-3 sm:gap-4">
        <h1 className="sr-only">Applications</h1>
        <div className="shrink-0">
          {/* Phones scroll the views sideways instead of hiding five of them. */}
          <div className="flex gap-2 overflow-x-auto pb-1 sm:grid sm:grid-cols-4 sm:overflow-visible sm:pb-0 xl:grid-cols-8">
            {kpiCards.map((metric) => {
              const selected = savedView === metric.view;
              return (
                <WorkspaceMetricTile
                  key={metric.label}
                  label={metric.label}
                  value={metric.value ?? "—"}
                  tone={
                    metric.value === null || metric.value === 0
                      ? "var(--text-tertiary)"
                      : metric.accent ?? "var(--text-primary)"
                  }
                  selected={selected}
                  highlighted={Boolean(metric.highlight && metric.value !== null && metric.value > 0)}
                  onClick={() => selectView(metric.view)}
                  className="max-sm:min-w-28 max-sm:shrink-0"
                />
              );
            })}
          </div>
        </div>

        <div className="card flex min-h-0 flex-1 flex-col overflow-hidden p-0">
          <div className="flex shrink-0 items-center gap-2 border-b border-[var(--border)] p-3">
            <input
              aria-label="Search applications"
              value={pipelineSearch}
              onChange={(event) => setPipelineSearch(event.target.value)}
              placeholder="Search"
              className="input"
            />
            {searchChanged || initialQuery ? (
              <button
                type="button"
                className="btn btn-secondary shrink-0"
                onClick={() => {
                  if (searchChanged) {
                    selectView(savedView, pipelineSearch);
                    return;
                  }

                  setPipelineSearch("");
                  selectView(savedView, "");
                }}
              >
                {searchChanged ? "Search" : "Clear"}
              </button>
            ) : null}
            {reviewQueueCount !== null && reviewQueueCount > 0 ? (
              <Link href="/app/review" className="btn btn-secondary shrink-0">
                Email review {reviewQueueCount}
              </Link>
            ) : null}
          </div>

          {sortedApplications.length === 0 ? (
            <div className="p-4 sm:p-6">
              <p className="text-sm font-medium text-[var(--text-primary)]">
                {initialQuery ? "No matches." : "Nothing here."}
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                {savedView !== "active" &&
                currentDashboard.metrics.activeApplications > 0 ? (
                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    onClick={() => selectView("active")}
                  >
                    See {currentDashboard.metrics.activeApplications} active
                  </button>
                ) : null}
                {savedView !== "all" && totalApplications > 0 ? (
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => selectView("all")}
                  >
                    See all {totalApplications}
                  </button>
                ) : null}
                {savedView !== "interviewing" &&
                currentDashboard.metrics.interviews > 0 ? (
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => selectView("interviewing")}
                  >
                    Interviews ({currentDashboard.metrics.interviews})
                  </button>
                ) : null}
              </div>
            </div>
          ) : (
            <DashboardApplicationList
              applications={sortedApplications}
              activeApplicationId={activeApplication?.id ?? null}
              pendingActions={currentDashboard.pendingActions}
              sortState={sortState}
              listRef={applicationListRef}
              timeZoneId={timeZoneId}
              onSort={selectSort}
              onOpenApplication={openApplicationFromRow}
            />
          )}
        </div>
      </div>

      <aside className="hidden xl:flex xl:min-h-0 xl:items-stretch">
        <div className="card application-detail-pane w-full p-4">
          {activeApplication ? (
            <DashboardApplicationEvidencePanel
              application={activeApplication}
              dashboard={currentDashboard}
              timeZoneId={timeZoneId}
              pendingCompletionKey={pendingCompletionKey}
              actionNotice={actionNotice}
              actionError={actionError}
              onCompleteAction={completeApplicationAction}
            />
          ) : (
            <DashboardEmptySelectedPanel viewLabel={SAVED_VIEW_LABEL[savedView]} />
          )}
        </div>
      </aside>
    </section>
  );
}

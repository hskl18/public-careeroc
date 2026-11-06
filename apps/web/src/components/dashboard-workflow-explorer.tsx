"use client";

import type { Route } from "next";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  DashboardApplicationEvidencePanel,
  DashboardEmptySelectedPanel,
} from "@/components/dashboard-application-evidence-panel";
import { DashboardApplicationList } from "@/components/dashboard-application-list";
import { WorkspaceMetricTile } from "@/components/workspace-controls";
import type {
  ApplicationCard,
  DashboardResponse,
  DashboardView,
} from "@/lib/dashboard";
import { readErrorMessage } from "@/lib/client-fetch";
import {
  completeApplicationActionLocally,
  sortApplications,
  type SortColumn,
  type SortState,
} from "@/lib/dashboard-workflow";

type WorkflowExplorerProps = {
  dashboard: DashboardResponse;
  timeZoneId: string;
  // The view the server loaded; the list below holds only that view's applications.
  view: DashboardView;
};

const SAVED_VIEW_LABEL: Record<DashboardView, string> = {
  "needs-action": "Needs action",
  "needs-review": "Fix",
  active: "Active",
  waiting: "Waiting",
  interviewing: "Interviews",
  offer: "Offers",
  rejected: "Rejected",
  all: "All",
};

function DashboardViewTiles({
  metrics,
  selectedView,
  onSelect,
}: {
  metrics: DashboardResponse["metrics"];
  selectedView: DashboardView;
  onSelect: (view: DashboardView) => void;
}) {
  const tiles: Array<{ view: DashboardView; value: number; accent: string; highlight?: boolean }> = [
    { view: "needs-action", value: metrics.needsAction, accent: "var(--accent)", highlight: true },
    // Applications whose company or role look misread; not the email review queue.
    { view: "needs-review", value: metrics.needsReview, accent: "var(--brand-blue-ink)" },
    { view: "active", value: metrics.activeApplications, accent: "var(--blue)" },
    { view: "waiting", value: metrics.waitingOnEmployer, accent: "var(--green)" },
    { view: "interviewing", value: metrics.interviews, accent: "var(--blue)" },
    { view: "offer", value: metrics.offers, accent: "var(--green)" },
    { view: "rejected", value: metrics.rejections, accent: "var(--red)" },
    { view: "all", value: metrics.totalApplications, accent: "var(--text-tertiary)" },
  ];

  return (
    // Phones scroll the views sideways instead of hiding five of them.
    <div className="flex gap-2 overflow-x-auto pb-1 sm:grid sm:grid-cols-4 sm:overflow-visible sm:pb-0 xl:grid-cols-8">
      {tiles.map((tile) => (
        <WorkspaceMetricTile
          key={tile.view}
          label={SAVED_VIEW_LABEL[tile.view]}
          value={tile.value}
          tone={tile.value === 0 ? "var(--text-tertiary)" : tile.accent}
          selected={selectedView === tile.view}
          highlighted={Boolean(tile.highlight && tile.value > 0)}
          onClick={() => onSelect(tile.view)}
          className="max-sm:min-w-28 max-sm:shrink-0"
        />
      ))}
    </div>
  );
}

function EmptyViewActions({
  metrics,
  selectedView,
  searching,
  onSelect,
}: {
  metrics: DashboardResponse["metrics"];
  selectedView: DashboardView;
  searching: boolean;
  onSelect: (view: DashboardView) => void;
}) {
  return (
    <div className="p-4 sm:p-6">
      <p className="text-sm font-medium text-[var(--text-primary)]">
        {searching ? "No matches." : "Nothing here."}
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        {selectedView !== "active" && metrics.activeApplications > 0 ? (
          <button type="button" className="btn btn-primary btn-sm" onClick={() => onSelect("active")}>
            Active ({metrics.activeApplications})
          </button>
        ) : null}
        {selectedView !== "all" && metrics.totalApplications > 0 ? (
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => onSelect("all")}>
            All ({metrics.totalApplications})
          </button>
        ) : null}
        {selectedView !== "interviewing" && metrics.interviews > 0 ? (
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => onSelect("interviewing")}>
            Interviews ({metrics.interviews})
          </button>
        ) : null}
      </div>
    </div>
  );
}

export function DashboardWorkflowExplorer({
  dashboard,
  timeZoneId,
  view,
}: WorkflowExplorerProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  // Tasks marked done here, shown until the refreshed dashboard arrives; a new dashboard prop
  // already reflects them, so the list belongs to the dashboard it was made against.
  const [completed, setCompleted] = useState<{
    base: DashboardResponse;
    actions: Array<{ applicationId: string; reminderId?: string }>;
  }>({ base: dashboard, actions: [] });
  const [pendingCompletionKey, setPendingCompletionKey] = useState<string | null>(
    null,
  );
  const [actionNotice, setActionNotice] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const currentDashboard = useMemo(
    () =>
      (completed.base === dashboard ? completed.actions : []).reduce(
        (current, action) =>
          completeApplicationActionLocally(current, action.applicationId, action.reminderId),
        dashboard,
      ),
    [completed, dashboard],
  );
  const initialQuery = useMemo(
    () => searchParams.get("q")?.trim() ?? "",
    [searchParams],
  );
  const [savedView, setSavedView] = useState<DashboardView>(view);
  const [pipelineSearch, setPipelineSearch] = useState(initialQuery);
  const [selectedApplicationId, setSelectedApplicationId] = useState<
    string | null
  >(null);
  const [sortState, setSortState] = useState<SortState>({ column: null, direction: "desc" });
  const applicationListRef = useRef<HTMLDivElement | null>(null);
  const normalizedPipelineSearch = pipelineSearch.trim();
  const searchChanged = normalizedPipelineSearch !== initialQuery;

  const sortedApplications = useMemo(
    () => sortApplications(currentDashboard.applications, sortState),
    [currentDashboard.applications, sortState],
  );

  const activeApplication =
    sortedApplications.find(
      (application) => application.id === selectedApplicationId,
    ) ??
    sortedApplications[0] ??
    null;

  const reviewQueueCount = currentDashboard.metrics.reviewQueue;

  function selectView(next: DashboardView, nextQuery = pipelineSearch) {
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

  async function completeApplicationAction(application: ApplicationCard, reminderId?: string) {
    const shouldMoveOaToApplied = application.status === "Oa Pending";
    if (!reminderId && !shouldMoveOaToApplied) {
      setActionNotice("No open task is attached to this action.");
      return;
    }

    const requestKey = `${application.id}:${reminderId ?? "stage"}:Completed`;
    setPendingCompletionKey(requestKey);
    setActionError(null);
    setActionNotice(null);

    try {
      if (reminderId) {
        const reminderResponse = await fetch(
          `/api/careeroc/api/reminders/${reminderId}/status`,
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

      setCompleted((current) => ({
        base: dashboard,
        actions: [
          ...(current.base === dashboard ? current.actions : []),
          { applicationId: application.id, reminderId },
        ],
      }));
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
          <DashboardViewTiles
            metrics={currentDashboard.metrics}
            selectedView={savedView}
            onSelect={(next) => selectView(next)}
          />
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
            {reviewQueueCount > 0 ? (
              <Link href="/app/review" className="btn btn-secondary shrink-0">
                Email review {reviewQueueCount}
              </Link>
            ) : null}
          </div>

          {sortedApplications.length === 0 ? (
            <EmptyViewActions
              metrics={currentDashboard.metrics}
              selectedView={savedView}
              searching={Boolean(initialQuery)}
              onSelect={(next) => selectView(next)}
            />
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

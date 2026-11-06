import type { Route } from "next";
import Link from "next/link";
import { requireUser } from "@/lib/require-user";
import { RecentChangesSection } from "@/components/recent-changes-section";
import type { RecentActivity } from "@/lib/dashboard";
import { getDashboardWithFiltersForUser } from "@/lib/dashboard-server";
import { getProfileForUser } from "@/lib/profile-server";

type UpdatesPageProps = {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
};

const activityPageSize = 50;

const activityFilters = [
  { label: "All", value: "all" },
  { label: "Interviews", value: "interviews" },
  { label: "Deadlines", value: "deadlines" },
  { label: "Review", value: "review" },
  { label: "Offers", value: "offers" },
] as const;

type ActivityFilter = (typeof activityFilters)[number]["value"];

export default async function UpdatesPage({ searchParams }: UpdatesPageProps) {
  const user = await requireUser();
  const resolvedSearchParams = (await searchParams) ?? {};
  const selectedType = normalizeActivityFilter(firstParam(resolvedSearchParams.type));
  const cursor = firstParam(resolvedSearchParams.cursor);
  const [{ dashboard, source }, { profile }] = await Promise.all([
    getDashboardWithFiltersForUser(
      {
        limit: activityPageSize,
        cursor,
      },
      user,
    ),
    getProfileForUser(user),
  ]);
  const filteredActivity = filterActivities(dashboard.recentActivity, selectedType);
  const nextHref = dashboard.nextCursor
    ? buildUpdatesHref(resolvedSearchParams, { cursor: dashboard.nextCursor })
    : null;
  const previousHref = cursor
    ? buildUpdatesHref(resolvedSearchParams, { cursor: null })
    : null;

  const coldStart =
    dashboard.inbox.lastSyncAtUtc === null &&
    dashboard.inbox.syncedEmails === 0 &&
    dashboard.inbox.processedEmails === 0;

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell flex w-full flex-col gap-4 px-3 py-4 sm:px-5 sm:py-6 animate-in">
        {source !== "api" ? (
          <div role="status" className="alert alert-warning shrink-0">
            API unavailable. Showing empty data.
          </div>
        ) : null}

        <section className="shrink-0">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <h1 className="text-xl font-semibold text-[var(--text-primary)]">All activity</h1>
          </div>
          <div className="mt-2 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <nav className="flex flex-wrap gap-2" aria-label="Activity filters">
              {activityFilters.map((filter) => {
                const selected = selectedType === filter.value;
                return (
                  <Link
                    key={filter.value}
                    href={buildUpdatesHref(resolvedSearchParams, {
                      type: filter.value,
                      cursor: null,
                    })}
                    className={`btn btn-sm ${
                      selected ? "btn-primary" : "btn-secondary"
                    }`}
                    aria-current={selected ? "page" : undefined}
                  >
                    {filter.label}
                  </Link>
                );
              })}
            </nav>
            <div className="flex flex-wrap items-center gap-2 text-xs text-[var(--text-secondary)]">
              <span className="tabular-nums text-[var(--text-tertiary)]">{filteredActivity.length}</span>
              {previousHref ? (
                <Link href={previousHref} className="btn btn-secondary btn-sm">
                  First page
                </Link>
              ) : null}
              {nextHref ? (
                <Link href={nextHref} className="btn btn-secondary btn-sm">
                  Next page
                </Link>
              ) : null}
            </div>
          </div>
        </section>

        <div className="workspace-scroll-region min-h-0 flex-1">
          <RecentChangesSection
            recentActivity={filteredActivity}
            coldStart={coldStart}
            timeZoneId={profile.timeZoneId}
            variant="full"
          />
        </div>
      </div>
    </main>
  );
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function normalizeActivityFilter(value: string | undefined): ActivityFilter {
  return activityFilters.some((filter) => filter.value === value)
    ? (value as ActivityFilter)
    : "all";
}

function filterActivities(activities: RecentActivity[], filter: ActivityFilter) {
  if (filter === "all") {
    return activities;
  }

  return activities.filter((activity) => {
    const category = normalizeCategory(activity.category);
    if (filter === "interviews") {
      return category.includes("interview");
    }
    if (filter === "deadlines") {
      return category.includes("oa") || category.includes("assessment") || category.includes("deadline");
    }
    if (filter === "review") {
      return category.includes("review") || category.includes("manual");
    }
    if (filter === "offers") {
      return category.includes("offer");
    }
    return true;
  });
}

function normalizeCategory(value: string) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/[\s-]+/g, "_")
    .toLowerCase();
}

function buildUpdatesHref(
  searchParams: Record<string, string | string[] | undefined>,
  updates: Partial<{
    type: ActivityFilter;
    cursor: string | null;
  }>,
) {
  const params = new URLSearchParams();

  Object.entries(searchParams).forEach(([key, value]) => {
    if (typeof value === "string") {
      params.set(key, value);
      return;
    }

    value?.forEach((item) => params.append(key, item));
  });

  if (updates.type) {
    if (updates.type === "all") {
      params.delete("type");
    } else {
      params.set("type", updates.type);
    }
  }

  if (Object.hasOwn(updates, "cursor")) {
    if (updates.cursor) {
      params.set("cursor", updates.cursor);
    } else {
      params.delete("cursor");
    }
  }

  const query = params.toString();
  return (query ? `/app/updates?${query}` : "/app/updates") as Route;
}

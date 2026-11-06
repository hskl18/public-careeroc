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
  // The dashboard carries the 50 newest updates across every application; paging it would page
  // applications, not updates, so this page shows those 50.
  const [{ dashboard, source }, { profile }] = await Promise.all([
    getDashboardWithFiltersForUser({}, user),
    getProfileForUser(user),
  ]);
  const filteredActivity = filterActivities(dashboard.recentActivity, selectedType);

  const coldStart =
    dashboard.inbox.lastSyncAtUtc === null &&
    dashboard.inbox.syncedEmails === 0 &&
    dashboard.inbox.processedEmails === 0;

  return (
    <main className="app-scroll-main">
      <div className="workspace-shell flex w-full flex-col gap-4 px-3 py-4 sm:px-5 sm:py-6 animate-in">
        {source !== "api" ? (
          <div role="status" className="alert alert-warning shrink-0">
            API offline. Showing empty data.
          </div>
        ) : null}

        <section className="shrink-0">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <h1 className="text-xl font-semibold text-[var(--text-primary)]">Latest activity</h1>
          </div>
          <div className="mt-2 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <nav className="flex flex-wrap gap-2" aria-label="Activity filters">
              {activityFilters.map((filter) => {
                const selected = selectedType === filter.value;
                return (
                  <Link
                    key={filter.value}
                    href={buildUpdatesHref(resolvedSearchParams, filter.value)}
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
              <span className="tabular-nums text-[var(--text-tertiary)]">
                {filteredActivity.length} of the latest {dashboard.recentActivity.length} updates
              </span>
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
  type: ActivityFilter,
) {
  const params = new URLSearchParams();

  Object.entries(searchParams).forEach(([key, value]) => {
    if (key === "cursor") return;
    if (typeof value === "string") {
      params.set(key, value);
      return;
    }

    value?.forEach((item) => params.append(key, item));
  });

  if (type === "all") {
    params.delete("type");
  } else {
    params.set("type", type);
  }

  const query = params.toString();
  return `/app/updates${query ? `?${query}` : ""}` as Route;
}

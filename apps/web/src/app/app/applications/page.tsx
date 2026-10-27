import { auth } from "@/auth";
import { DashboardWorkflowExplorer } from "@/components/dashboard-workflow-explorer";
import type { DashboardView } from "@/lib/dashboard";
import { getDashboardWithFiltersForUser } from "@/lib/dashboard-server";
import { getProfileForUser } from "@/lib/profile-server";

type ApplicationsPageProps = {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
};

const DASHBOARD_VIEWS: readonly DashboardView[] = [
  "needs-action",
  "needs-review",
  "active",
  "waiting",
  "interviewing",
  "offer",
  "rejected",
  "all",
];

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function normalizeDashboardView(value: string | string[] | undefined): DashboardView {
  const normalized = firstParam(value);
  return normalized && (DASHBOARD_VIEWS as readonly string[]).includes(normalized)
    ? (normalized as DashboardView)
    : "needs-action";
}

export default async function ApplicationsPage({
  searchParams,
}: ApplicationsPageProps) {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const resolvedSearchParams = (await searchParams) ?? {};
  const selectedView = normalizeDashboardView(
    resolvedSearchParams.view ?? resolvedSearchParams.focus,
  );
  const explorerKey = [
    selectedView,
    firstParam(resolvedSearchParams.q) ?? "",
    firstParam(resolvedSearchParams.company) ?? "",
    firstParam(resolvedSearchParams.roleCanonical) ?? "",
    firstParam(resolvedSearchParams.source) ?? "",
    firstParam(resolvedSearchParams.location) ?? "",
    firstParam(resolvedSearchParams.cursor) ?? "",
  ].join(":");
  const [{ dashboard, source }, { profile }] = await Promise.all([
    getDashboardWithFiltersForUser(
      {
        view: selectedView,
        q: firstParam(resolvedSearchParams.q),
        company: firstParam(resolvedSearchParams.company),
        roleCanonical: firstParam(resolvedSearchParams.roleCanonical),
        source: firstParam(resolvedSearchParams.source),
        location: firstParam(resolvedSearchParams.location),
        cursor: firstParam(resolvedSearchParams.cursor),
        limit: selectedView === "rejected" ? 250 : undefined,
      },
      user,
    ),
    getProfileForUser(user),
  ]);

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col px-3 py-4 animate-in sm:px-5 sm:py-6 md:h-full md:overflow-hidden">
        {source === "fallback" ? (
          <div className="alert alert-warning">
            Live API data is unavailable right now. This page is showing an
            empty local state until the API comes back.
          </div>
        ) : null}

        <DashboardWorkflowExplorer
          key={explorerKey}
          dashboard={dashboard}
          timeZoneId={profile.timeZoneId}
          variant="page"
        />
      </div>
    </main>
  );
}

import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import type { DashboardResponse, DashboardView } from "./dashboard";
import { createKeyedUserScopedCache } from "./server-user-cache";

export type DashboardRequestFilters = {
  view?: DashboardView;
  limit?: number;
  cursor?: string | null;
  q?: string | null;
  company?: string | null;
  roleCanonical?: string | null;
  source?: string | null;
  location?: string | null;
};

function buildEmptyDashboard(user: CareerocApiUser): DashboardResponse {
  return {
    metrics: {
      totalApplications: 0,
      activeApplications: 0,
      needsAction: 0,
      needsReview: 0,
      waitingOnEmployer: 0,
      interviews: 0,
      offers: 0,
      rejections: 0,
      reviewQueue: 0,
      closedApplications: 0,
    },
    inbox: {
      connectedAddress: user.email,
      syncedEmails: 0,
      processedEmails: 0,
      lastSyncAtUtc: null,
      pendingAnalysisEmails: 0,
      processingAnalysisEmails: 0,
      failedAnalysisEmails: 0,
      lastAnalysisCompletedAtUtc: null,
      lastAnalysisError: null,
      scannedCount: 0,
      totalEstimate: null,
      recognizedApplications: 0,
      backfillStatus: null,
    },
    applications: [],
    pendingActions: [],
    recentActivity: [],
  };
}

export async function getDashboardForUser(user: CareerocApiUser): Promise<{
  dashboard: DashboardResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getDashboardWithFiltersForUser({}, user);
}

export async function getDashboardWithFiltersForUser(
  filters: DashboardRequestFilters,
  user: CareerocApiUser,
): Promise<{
  dashboard: DashboardResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getDashboardWithFiltersForUserCached(
    serializeDashboardFilters(filters),
    user,
  );
}

const getDashboardWithFiltersForUserCached = createKeyedUserScopedCache(
  async (
    filtersKey,
    user,
  ): Promise<{
    dashboard: DashboardResponse;
    source: "api" | "fallback";
    proxyPrefix: string | null;
  }> => {
    const filters = parseDashboardFilters(filtersKey);
    const backendBaseUrl = getCareerocBackendBaseUrl();

    if (!backendBaseUrl) {
      return {
        dashboard: buildEmptyDashboard(user),
        source: "fallback",
        proxyPrefix: null,
      };
    }

    try {
      const searchParams = new URLSearchParams();
      if (filters.view && filters.view !== "all") {
        searchParams.set("view", filters.view);
      }
      if (filters.limit) {
        searchParams.set("limit", filters.limit.toString());
      }
      if (filters.cursor) {
        searchParams.set("cursor", filters.cursor);
      }
      for (const [key, value] of Object.entries({
        q: filters.q,
        company: filters.company,
        roleCanonical: filters.roleCanonical,
        source: filters.source,
        location: filters.location,
      })) {
        if (value?.trim()) {
          searchParams.set(key, value.trim());
        }
      }

      const pathAndQuery = `/api/dashboard${searchParams.size > 0 ? `?${searchParams.toString()}` : ""}`;
      const response = await fetchCareerocApi(pathAndQuery, user, {
        cache: "no-store",
      });

      if (!response.ok) {
        throw new Error(`Dashboard API returned ${response.status}`);
      }

      const dashboard = (await response.json()) as DashboardResponse;
      return {
        dashboard,
        source: "api",
        proxyPrefix: getCareerocClientApiBaseUrl(),
      };
    } catch {
      return {
        dashboard: buildEmptyDashboard(user),
        source: "fallback",
        proxyPrefix: getCareerocClientApiBaseUrl(),
      };
    }
  },
);

function serializeDashboardFilters(filters: DashboardRequestFilters) {
  return JSON.stringify({
    view: filters.view ?? null,
    limit: filters.limit ?? null,
    cursor: filters.cursor ?? null,
    q: filters.q?.trim() || null,
    company: filters.company?.trim() || null,
    roleCanonical: filters.roleCanonical?.trim() || null,
    source: filters.source?.trim() || null,
    location: filters.location?.trim() || null,
  });
}

function parseDashboardFilters(filtersKey: string): DashboardRequestFilters {
  return JSON.parse(filtersKey) as DashboardRequestFilters;
}

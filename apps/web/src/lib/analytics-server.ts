import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import type {
  AnalyticsProvider,
  AnalyticsRange,
  AnalyticsResponse,
} from "./analytics";
import { createKeyedUserScopedCache } from "./server-user-cache";

const fallbackAnalytics: AnalyticsResponse = {
  metrics: {
    applicationsCount: 0,
    uniqueCompanies: 0,
    rolesCount: 0,
    replyRate: 0,
    interviewRate: 0,
    offerRate: 0,
    avgTimeToFirstResponseHours: null,
  },
  companyBreakdown: [],
  roleBreakdown: [],
};

export async function getAnalyticsWithFiltersForUser(
  filters: {
    range?: AnalyticsRange;
    provider?: AnalyticsProvider;
  },
  user: CareerocApiUser,
): Promise<{
  analytics: AnalyticsResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getAnalyticsWithFiltersForUserCached(
    serializeAnalyticsFilters(filters),
    user,
  );
}

const getAnalyticsWithFiltersForUserCached = createKeyedUserScopedCache(async (
  filtersKey,
  user,
): Promise<{
  analytics: AnalyticsResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const filters = parseAnalyticsFilters(filtersKey);
  const backendBaseUrl = getCareerocBackendBaseUrl();
  const range = filters.range ?? "all";
  const provider = filters.provider ?? "all";

  if (!backendBaseUrl) {
    return { analytics: fallbackAnalytics, source: "fallback", proxyPrefix: null };
  }

  try {
    const searchParams = new URLSearchParams();
    if (range !== "all") {
      searchParams.set("range", range);
    }

    if (provider !== "all") {
      searchParams.set("provider", provider);
    }

    const pathAndQuery = `/api/analytics${searchParams.size > 0 ? `?${searchParams.toString()}` : ""}`;
    const response = await fetchCareerocApi(pathAndQuery, user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Analytics API returned ${response.status}`);
    }

    const analytics = (await response.json()) as AnalyticsResponse;
    return {
      analytics,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      analytics: fallbackAnalytics,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

function serializeAnalyticsFilters(filters: {
  range?: AnalyticsRange;
  provider?: AnalyticsProvider;
}) {
  return JSON.stringify({
    range: filters.range ?? null,
    provider: filters.provider ?? null,
  });
}

function parseAnalyticsFilters(filtersKey: string) {
  return JSON.parse(filtersKey) as {
    range?: AnalyticsRange | null;
    provider?: AnalyticsProvider | null;
  };
}

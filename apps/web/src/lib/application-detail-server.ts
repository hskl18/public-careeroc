import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import type { ApplicationDetail } from "./application-detail";
import { createKeyedUserScopedCache } from "./server-user-cache";

export async function getApplicationDetailForUser(
  user: CareerocApiUser,
  applicationId: string,
): Promise<{
  detail: ApplicationDetail | null;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getApplicationDetailForUserCached(
    normalizeApplicationId(applicationId),
    user,
  );
}

const getApplicationDetailForUserCached = createKeyedUserScopedCache(async (
  applicationId,
  user,
): Promise<{
  detail: ApplicationDetail | null;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return { detail: null, source: "fallback", proxyPrefix: null };
  }

  try {
    const response = await fetchCareerocApi(`/api/applications/${applicationId}`, user, {
      cache: "no-store",
    });

    if (response.status === 404) {
      return {
        detail: null,
        source: "api",
        proxyPrefix: getCareerocClientApiBaseUrl(),
      };
    }

    if (!response.ok) {
      throw new Error(`Application detail API returned ${response.status}`);
    }

    const detail = (await response.json()) as ApplicationDetail;
    return {
      detail,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      detail: null,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

function normalizeApplicationId(applicationId: string) {
  return applicationId.trim();
}

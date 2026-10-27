import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import {
  mapRecruitingContactListResponse,
  type RecruitingContactListResponse,
} from "./recruiting-contacts";
import { createUserScopedCache } from "./server-user-cache";

const fallbackContacts: RecruitingContactListResponse = { contacts: [] };

export async function getRecruitingContactsForUser(
  user: CareerocApiUser,
): Promise<{
  contacts: RecruitingContactListResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getRecruitingContactsForUserCached(user);
}

const getRecruitingContactsForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  contacts: RecruitingContactListResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return { contacts: fallbackContacts, source: "fallback", proxyPrefix: null };
  }

  try {
    const response = await fetchCareerocApi("/api/recruiting-contacts?limit=50", user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Recruiting contacts API returned ${response.status}`);
    }

    return {
      contacts: mapRecruitingContactListResponse(await response.json()),
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      contacts: fallbackContacts,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

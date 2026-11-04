import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import type { InboxSetupResponse } from "./inbox-accounts";
import { createUserScopedCache } from "./server-user-cache";

const fallbackSetup: InboxSetupResponse = { accounts: [] };

export async function getInboxSetupForUser(user: CareerocApiUser): Promise<{
  setup: InboxSetupResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getInboxSetupForUserCached(user);
}

const getInboxSetupForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  setup: InboxSetupResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return { setup: fallbackSetup, source: "fallback", proxyPrefix: null };
  }

  try {
    const response = await fetchCareerocApi("/api/inbox-accounts", user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Inbox accounts API returned ${response.status}`);
    }

    const setup = (await response.json()) as InboxSetupResponse;
    return {
      setup,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      setup: fallbackSetup,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

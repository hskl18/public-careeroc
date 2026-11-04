import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import { fallbackResumeWorkspace, type ResumeWorkspace } from "./resumes";
import { createUserScopedCache } from "./server-user-cache";

export async function getResumeWorkspaceForUser(user: CareerocApiUser): Promise<{
  workspace: ResumeWorkspace;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getResumeWorkspaceForUserCached(user);
}

const getResumeWorkspaceForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  workspace: ResumeWorkspace;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return {
      workspace: fallbackResumeWorkspace,
      source: "fallback",
      proxyPrefix: null,
    };
  }

  try {
    const response = await fetchCareerocApi("/api/resumes", user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Resume API returned ${response.status}`);
    }

    return {
      workspace: (await response.json()) as ResumeWorkspace,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      workspace: fallbackResumeWorkspace,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

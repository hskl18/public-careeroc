import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import { buildFallbackProfile, type UserMemoryEntry, type UserProfile } from "./profile";
import { createUserScopedCache } from "./server-user-cache";

function normalizeProfile(profile: UserProfile): UserProfile {
  return {
    ...profile,
    profileIcon: profile.profileIcon ?? "inbox-blue",
    welcomeDismissedAtUtc: profile.welcomeDismissedAtUtc ?? null,
  };
}

export async function getProfileForUser(user: CareerocApiUser): Promise<{
  profile: UserProfile;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getProfileForUserCached(user);
}

const getProfileForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  profile: UserProfile;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return {
      profile: buildFallbackProfile(user),
      source: "fallback",
      proxyPrefix: null,
    };
  }

  try {
    const response = await fetchCareerocApi("/api/profile", user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Profile API returned ${response.status}`);
    }

    const profile = normalizeProfile((await response.json()) as UserProfile);
    return {
      profile,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      profile: buildFallbackProfile(user),
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

export async function getMemoryReviewForUser(user: CareerocApiUser): Promise<{
  profile: UserProfile;
  pendingMemory: UserMemoryEntry[];
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getMemoryReviewForUserCached(user);
}

const getMemoryReviewForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  profile: UserProfile;
  pendingMemory: UserMemoryEntry[];
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return {
      profile: buildFallbackProfile(user),
      pendingMemory: [],
      source: "fallback",
      proxyPrefix: null,
    };
  }

  try {
    const [profileResponse, pendingResponse] = await Promise.all([
      getProfileForUser(user),
      fetchCareerocApi("/api/profile/memory/proposals", user, { cache: "no-store" }),
    ]);

    if (profileResponse.source !== "api") {
      throw new Error("Profile API returned fallback data.");
    }

    const profile = profileResponse.profile;
    const pendingMemory = pendingResponse.ok
      ? ((await pendingResponse.json()) as UserMemoryEntry[])
      : [];

    return {
      profile,
      pendingMemory,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      profile: buildFallbackProfile(user),
      pendingMemory: [],
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

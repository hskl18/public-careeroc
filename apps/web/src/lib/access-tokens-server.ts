import "server-only";

import type { CareerocApiUser } from "./api-user";
import { fetchCareerocApi, getCareerocBackendBaseUrl } from "./careeroc-api-server";
import type { AccessToken } from "@/components/assistant-access-panel";

export async function getAccessTokensForUser(user: CareerocApiUser): Promise<AccessToken[]> {
  if (!getCareerocBackendBaseUrl()) {
    return [];
  }

  try {
    const response = await fetchCareerocApi("/api/profile/access-tokens", user, { cache: "no-store" });
    return response.ok ? ((await response.json()) as AccessToken[]) : [];
  } catch {
    return [];
  }
}

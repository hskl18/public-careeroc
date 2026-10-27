import "server-only";

import type { CareerocApiUser } from "./api-user";
import { fetchCareerocApi } from "./careeroc-api-server";
import { createUserScopedCache } from "./server-user-cache";

export type AdminSummary = {
  queue: AdminQueueSummary;
  recentModelCallEvents: AdminModelCallEvent[];
  operationalChecks: AdminOperationalCheck[];
  recentFailedEmailJobs: AdminFailedEmailJob[];
};

export type AdminQueueSummary = {
  pendingAnalysisJobs: number;
  processingAnalysisJobs: number;
  failedAnalysisJobs: number;
};

export type AdminModelCallEvent = {
  id: string;
  occurredAtUtc: string;
  provider: string;
  model: string;
  purpose: string;
  outcome: string;
  userEmail: string | null;
  inboxAccountId: string | null;
  inboxEmailId: string | null;
  runId: string | null;
  correlationId: string | null;
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  estimatedCostUsd: number | null;
  latencyMs: number;
  statusCode: number | null;
  errorType: string | null;
};

export type AdminOperationalCheck = {
  area: string;
  name: string;
  status: string;
  detail: string;
  currentValue: string | null;
};

export type AdminFailedEmailJob = {
  jobId: string;
  inboxEmailId: string;
  inboxAccountId: string;
  userEmail: string;
  inboxEmailAddress: string;
  attempts: number;
  updatedAtUtc: string;
  lockedAtUtc: string | null;
  lastError: string | null;
};

export const getAdminSummaryForUser = createUserScopedCache(async (
  user,
): Promise<{
  summary: AdminSummary | null;
  status: number;
}> => {
  let response: Response;
  try {
    response = await fetchCareerocApi("/api/admin/summary", user, {
      cache: "no-store",
    });
  } catch (error) {
    console.error("Admin summary request failed before receiving a response.", error);
    return { summary: null, status: 503 };
  }

  if (response.status === 403 || response.status === 401) {
    return { summary: null, status: response.status };
  }

  if (!response.ok) {
    throw new Error("Admin summary request failed.");
  }

  return {
    summary: (await response.json()) as AdminSummary,
    status: response.status,
  };
});

export async function getAdminAccessForUser(user: CareerocApiUser): Promise<boolean> {
  return getAdminAccessForUserCached(user);
}

const getAdminAccessForUserCached = createUserScopedCache(async (user) => {
  try {
    const response = await fetchCareerocApi("/api/admin/health", user, {
      cache: "no-store",
    });

    return response.ok;
  } catch {
    return false;
  }
});

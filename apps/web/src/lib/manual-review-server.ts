import {
  fetchCareerocApi,
  getCareerocBackendBaseUrl,
  getCareerocClientApiBaseUrl,
} from "./careeroc-api-server";
import type { CareerocApiUser } from "./api-user";
import type { ManualReviewResponse } from "./manual-review";
import { createUserScopedCache } from "./server-user-cache";

const fallbackManualReview: ManualReviewResponse = {
  emails: [],
  artifacts: [],
  applications: [],
  categoryOptions: [
    "recruiter_outreach",
    "application_received",
    "oa_invitation",
    "interview_invitation",
    "interview_confirmation",
    "rejection",
    "offer",
    "generic_update",
  ],
  statusOptions: [
    "Applied",
    "Recruiter Contact",
    "Oa Pending",
    "Interviewing",
    "Offer",
    "Rejected",
    "Closed",
  ],
  totalWaiting: 0,
  totalUnassigned: 0,
  totalArtifactReviews: 0,
};

export async function getManualReviewForUser(user: CareerocApiUser): Promise<{
  review: ManualReviewResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> {
  return getManualReviewForUserCached(user);
}

const getManualReviewForUserCached = createUserScopedCache(async (
  user,
): Promise<{
  review: ManualReviewResponse;
  source: "api" | "fallback";
  proxyPrefix: string | null;
}> => {
  const backendBaseUrl = getCareerocBackendBaseUrl();

  if (!backendBaseUrl) {
    return { review: fallbackManualReview, source: "fallback", proxyPrefix: null };
  }

  try {
    const response = await fetchCareerocApi("/api/review", user, {
      cache: "no-store",
    });

    if (!response.ok) {
      throw new Error(`Manual review API returned ${response.status}`);
    }

    const review = (await response.json()) as ManualReviewResponse;
    return {
      review,
      source: "api",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  } catch {
    return {
      review: fallbackManualReview,
      source: "fallback",
      proxyPrefix: getCareerocClientApiBaseUrl(),
    };
  }
});

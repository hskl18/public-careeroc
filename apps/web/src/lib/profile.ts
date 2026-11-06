import type { components } from "./api-schema";
import type { CareerocApiUser } from "./api-user";

type Schemas = components["schemas"];

export type UserProfile = Schemas["UserProfileDto"];
export type UserMemoryEntry = Schemas["UserMemoryEntryDto"];
export type UserProfileEvaluation = Schemas["UserProfileEvaluationDto"];
export type UserProfileEvaluationEvidence = Schemas["UserProfileEvaluationEvidenceDto"];
export type ResumeMailboxReviewEvidence = Schemas["ResumeMailboxReviewEvidenceDto"];
export type ResumeMailboxReview = Schemas["ResumeMailboxReviewDto"];
export type ProposedReminder = Schemas["ProposedReminderDto"];
export type ReviewAgentTrace = Schemas["ReviewAgentTraceDto"];
export type ReviewAgentTraceStep = Schemas["ReviewAgentTraceStepDto"];
export type ReviewAgentTraceFact = Schemas["ReviewAgentTraceFactDto"];
export type ReviewAgentTraceSummary = Schemas["AgentTraceSummaryDto"];
export type UpdateUserProfileRequest = Schemas["UpdateUserProfileRequest"];

export function buildFallbackProfile(user: CareerocApiUser): UserProfile {
  const email = user.email.trim();
  return {
    email,
    name: user.name?.trim() || email.split("@")[0] || "careeroc user",
    timeZoneId: "America/Los_Angeles",
    profileIcon: "inbox-blue",
    location: null,
    targetRole: null,
    welcomeDismissedAtUtc: null,
    createdAtUtc: new Date(0).toISOString(),
    memory: [],
    evaluation: {
      readinessScore: 0,
      summary: "Profile evaluation is unavailable until the live API is connected.",
      strengths: [],
      gaps: ["Live API data is unavailable."],
      nextActions: ["Reconnect the API before trusting candidate evaluation."],
      evidence: {
        totalApplications: 0,
        activeApplications: 0,
        interviewingApplications: 0,
        offers: 0,
        connectedInboxes: 0,
        totalInboxes: 0,
        memoryEntries: 0,
        lastApplicationActivityAtUtc: null,
        resumeVersions: 0,
        latestResumeScore: null,
        latestResumeTitle: null,
        resumeMailboxSummary: null,
      },
    },
  };
}

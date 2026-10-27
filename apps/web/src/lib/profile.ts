import type { CareerocApiUser } from "./api-user";

export type UserProfile = {
  email: string;
  name: string;
  timeZoneId: string;
  profileIcon: string;
  location: string | null;
  targetRole: string | null;
  welcomeDismissedAtUtc: string | null;
  createdAtUtc: string;
  evaluation: UserProfileEvaluation;
  memory: UserMemoryEntry[];
};

export type UserMemoryEntry = {
  id: string;
  kind: string;
  key: string;
  value: string;
  summary: string | null;
  sourceType: string | null;
  sourceId: string | null;
  confidence: number;
  confirmationStatus: string;
  updatedAtUtc: string;
  expiresAtUtc?: string | null;
};

export type UserProfileEvaluation = {
  readinessScore: number;
  summary: string;
  strengths: string[];
  gaps: string[];
  nextActions: string[];
  evidence: UserProfileEvaluationEvidence;
};

export type UserProfileEvaluationEvidence = {
  totalApplications: number;
  activeApplications: number;
  interviewingApplications: number;
  offers: number;
  connectedInboxes: number;
  totalInboxes: number;
  memoryEntries: number;
  lastApplicationActivityAtUtc: string | null;
  resumeVersions: number;
  latestResumeScore: number | null;
  latestResumeTitle: string | null;
  resumeMailboxSummary: string | null;
};

export type ResumeMailboxReviewEvidence = {
  resumeVersions: number;
  latestResumeScore: number;
  totalApplications: number;
  interviewOrOfferApplications: number;
  rejectedApplications: number;
  resumeVersionLinkedToOutcome: string | null;
};

export type ResumeMailboxReview = {
  generatedAtUtc: string;
  runtimePath: string;
  modelPath: string;
  summary: string;
  findings: string[];
  nextActions: string[];
  evidence: ResumeMailboxReviewEvidence;
  threadId: string | null;
  status: string;
  requiresHumanReview: boolean;
  confidence: number | null;
  trace: ReviewAgentTrace | null;
  proposedReminders: ProposedReminder[];
  remindersCreated: number | null;
};

export type ProposedReminder = {
  applicationId: string;
  company: string;
  role: string;
  title: string;
  dueInDays: number;
};

export type ReviewAgentTrace = {
  generatedAtUtc: string | null;
  steps: ReviewAgentTraceStep[];
  summary: ReviewAgentTraceSummary | null;
};

export type ReviewAgentTraceStep = {
  stage: string;
  agentName: string;
  summary: string;
  reason: string | null;
  facts: ReviewAgentTraceFact[];
};

export type ReviewAgentTraceFact = {
  label: string;
  value: string;
};

export type ReviewAgentTraceSummary = {
  modelPath: string | null;
  purpose: string;
  confidence: number | null;
  evidenceSource: string;
  reviewGateResult: string;
  fallbackPath: string;
};

export type UpdateUserProfileRequest = {
  name: string;
  timeZoneId: string;
  profileIcon: string;
  location: string | null;
  targetRole: string | null;
};

export function buildFallbackProfile(user: CareerocApiUser): UserProfile {
  const email = user.email.trim() || "local@careeroc.dev";
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

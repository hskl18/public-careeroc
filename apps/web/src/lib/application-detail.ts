import type { UserMemoryEntry } from "./profile";

export type ApplicationAgentBrief = {
  headline: string;
  summary: string;
  nextAction: string;
  recommendedSurface: string;
  confidenceSummary: string;
};

export type AgentTraceSummary = {
  modelPath: string | null;
  purpose: string;
  confidence: number | null;
  evidenceSource: string;
  reviewGateResult: string;
  fallbackPath: string;
};

export type AgentTraceFact = {
  label: string;
  value: string;
};

export type AgentTraceStep = {
  stage: string;
  agentName: string;
  summary: string;
  reason: string | null;
  facts: AgentTraceFact[];
};

export type AgentTrace = {
  generatedAtUtc: string | null;
  steps: AgentTraceStep[];
  summary: AgentTraceSummary | null;
};

export type ApplicationDetailReminder = {
  id: string;
  title: string;
  dueAtUtc: string;
  source: string;
  status: string;
  sourceEmailId: string | null;
  sourceThreadId: string | null;
  sourceMessageId: string | null;
  hasExplicitDueDate: boolean;
  gmailUrl: string | null;
};

export type ApplicationDetailActivity = {
  id: string;
  description: string;
  category: string;
  occurredAtUtc: string;
};

export type ApplicationContact = {
  name: string | null;
  email: string | null;
  type: string | null;
  roleHint: string | null;
  lastSeenAtUtc: string;
  latestSubject: string;
  messageCount: number;
};

export type ApplicationThreadMessage = {
  id: string;
  sender: string;
  subject: string;
  summary: string;
  snippet: string;
  bodyPreview: string;
  receivedAtUtc: string;
  isOutbound: boolean;
  category: string | null;
  actionRequired: boolean;
  dueDateUtc: string | null;
  classificationConfidence: number | null;
  matchingConfidence: number | null;
  contactName: string | null;
  contactEmail: string | null;
  contactType: string | null;
  contactRoleHint: string | null;
  requiresManualReview: boolean;
  reviewReason: string | null;
  processingSource: string | null;
  hasFeedbackNote: boolean;
  gmailUrl: string | null;
  traceSummary: AgentTraceSummary | null;
  agentTrace: AgentTrace | null;
};

export type ApplicationThread = {
  threadId: string;
  title: string;
  lastMessageAtUtc: string;
  messageCount: number;
  actionRequired: boolean;
  requiresManualReview: boolean;
  hasOutboundReply: boolean;
  stageLabel: string | null;
  snippet: string;
  messages: ApplicationThreadMessage[];
  gmailUrl: string | null;
};

export type ApplicationFeedbackNote = {
  emailId: string;
  subject: string;
  note: string;
  source: string;
  occurredAtUtc: string;
};

export type ApplicationArtifactEvidence = {
  id: string;
  jobApplicationId: string;
  artifactKind: string;
  fileName: string;
  contentType: string;
  safeTitle: string;
  extractedFactType: string;
  extractedFactText: string;
  normalizedDateUtc: string | null;
  confidence: number;
  sourceSnippet: string;
  requiresReview: boolean;
  reviewReason: string | null;
  createdAtUtc: string;
  traceSummary: AgentTraceSummary;
  agentTrace: AgentTrace | null;
};

export type ApplicationDetail = {
  id: string;
  company: string;
  role: string;
  status: string;
  completedStages: string[];
  priority: number;
  source: string;
  createdAtUtc: string;
  lastActivityAtUtc: string;
  actionRequired: boolean;
  agentBrief: ApplicationAgentBrief;
  contacts: ApplicationContact[];
  pendingActions: ApplicationDetailReminder[];
  recentActivity: ApplicationDetailActivity[];
  recentActivityTotalCount?: number;
  threads: ApplicationThread[];
  feedbackNotes: ApplicationFeedbackNote[];
  artifactEvidence: ApplicationArtifactEvidence[] | null;
  artifactEvidenceTotalCount?: number;
  memoryProposals?: UserMemoryEntry[] | null;
  agentTrace?: AgentTrace | null;
};

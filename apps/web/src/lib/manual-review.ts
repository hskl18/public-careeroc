export type ReviewApplication = {
  id: string;
  company: string;
  role: string;
  status: string;
  priority: number;
  lastActivityAtUtc: string;
};

export type ReviewEmail = {
  id: string;
  threadId: string;
  sender: string;
  subject: string;
  snippet: string;
  bodyPreview: string;
  receivedAtUtc: string;
  classificationConfidence: number | null;
  matchingConfidence: number | null;
  requiresManualReview: boolean;
  reviewReason: string | null;
  category: string | null;
  extractedCompany: string | null;
  extractedRole: string | null;
  contactName: string | null;
  contactEmail: string | null;
  contactType: string | null;
  contactRoleHint: string | null;
  actionRequired: boolean;
  dueDateUtc: string | null;
  notes: string | null;
  processingSource: string | null;
  agentTrace: ReviewAgentTrace | null;
  jobApplicationId: string | null;
  threadMessages: ReviewThreadMessage[];
  traceSummary: AgentTraceSummary | null;
};

export type ReviewArtifactEvidence = {
  id: string;
  jobApplicationId: string;
  company: string;
  role: string;
  artifactKind: string;
  fileName: string;
  safeTitle: string;
  extractedFactType: string;
  extractedFactText: string;
  normalizedDateUtc: string | null;
  confidence: number;
  sourceSnippet: string;
  reviewReason: string;
  createdAtUtc: string;
  traceSummary: AgentTraceSummary;
  agentTrace: ReviewAgentTrace | null;
};

export type ReviewApplicationArtifactRequest = {
  outcome: "keep_evidence" | "dismiss_evidence";
  notes?: string | null;
};

export type ReviewThreadMessage = {
  id: string;
  sender: string;
  subject: string;
  snippet: string;
  receivedAtUtc: string;
  category: string | null;
  requiresManualReview: boolean;
  isCurrentEmail: boolean;
};

export type ReviewAgentTraceFact = {
  label: string;
  value: string;
};

export type ReviewAgentTraceStep = {
  stage: string;
  agentName: string;
  summary: string;
  reason: string | null;
  facts: ReviewAgentTraceFact[];
};

export type ReviewAgentTrace = {
  generatedAtUtc: string | null;
  steps: ReviewAgentTraceStep[];
  summary: AgentTraceSummary | null;
};

export type AgentTraceSummary = {
  modelPath: string | null;
  purpose: string;
  confidence: number | null;
  evidenceSource: string;
  reviewGateResult: string;
  fallbackPath: string;
};

export type ManualReviewResponse = {
  emails: ReviewEmail[];
  artifacts: ReviewArtifactEvidence[];
  applications: ReviewApplication[];
  categoryOptions: string[];
  statusOptions: string[];
  totalWaiting: number;
  totalUnassigned: number;
  totalArtifactReviews: number;
};

export type DashboardMetrics = {
  totalApplications: number;
  activeApplications: number;
  needsAction: number;
  needsReview: number;
  waitingOnEmployer: number;
  interviews: number;
  offers: number;
  rejections: number;
  reviewQueue: number;
  closedApplications: number;
};

export type InboxOverview = {
  connectedAddress: string;
  syncedEmails: number;
  processedEmails: number;
  lastSyncAtUtc: string | null;
  pendingAnalysisEmails?: number;
  processingAnalysisEmails?: number;
  failedAnalysisEmails?: number;
  lastAnalysisCompletedAtUtc?: string | null;
  lastAnalysisError?: string | null;
  scannedCount?: number;
  totalEstimate?: number | null;
  recognizedApplications?: number;
  backfillStatus?: string | null;
};

export type ApplicationCard = {
  id: string;
  company: string;
  role: string;
  status: string;
  priority: number;
  receivedAtUtc: string;
  lastActivityAtUtc: string;
  updatedFrom: string;
  actionRequired: boolean;
  gmailUrl: string | null;
  hasOutboundReply: boolean;
  lastOutboundReplyAtUtc: string | null;
  nextAction: ApplicationNextAction;
  urgencyScore?: number | null;
  nextActionDueAtUtc?: string | null;
  priorityLabel: string;
  priorityReason: string;
  parserConfidence: number | null;
  requiresReview: boolean;
  reviewReason?: string | null;
  latestEvidence?: ApplicationLatestEvidence | null;
};

export type ApplicationNextAction = {
  title: string;
  dueAtUtc: string | null;
  hasExplicitDueDate: boolean;
  tone: "action" | "waiting" | "review" | "muted";
  sourceReminderId: string | null;
};

export type ApplicationLatestEvidence = {
  emailId: string;
  summary: string;
  subject: string | null;
  sender: string | null;
  snippet: string | null;
  occurredAtUtc: string;
  category: string;
  gmailUrl: string | null;
};

export type PendingAction = {
  id: string;
  jobApplicationId: string;
  company: string;
  role: string;
  title: string;
  dueAtUtc: string;
  source: string;
  status: string;
  sourceEmailId: string | null;
  sourceThreadId: string | null;
  sourceMessageId: string | null;
  hasExplicitDueDate: boolean;
  gmailUrl: string | null;
  taskType: "assessment" | "interview" | "reply" | "offer" | "follow_up" | "other";
  sourceSubject: string | null;
  sourceSender: string | null;
  receivedAtUtc: string | null;
  deadlineFound: boolean;
  deadlineHint: string | null;
};

export type RecentActivity = {
  id: string;
  jobApplicationId: string;
  company: string;
  role: string;
  description: string;
  category: string;
  receivedAtUtc: string;
  occurredAtUtc: string;
  importanceScore: number;
};

export type DashboardResponse = {
  metrics: DashboardMetrics;
  inbox: InboxOverview;
  applications: ApplicationCard[];
  pendingActions: PendingAction[];
  recentActivity: RecentActivity[];
  nextCursor?: string | null;
};

export type DashboardView =
  | "needs-action"
  | "needs-review"
  | "active"
  | "waiting"
  | "interviewing"
  | "offer"
  | "rejected"
  | "all";

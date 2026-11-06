import type {
  ApplicationDetail,
  ApplicationDetailReminder,
  ApplicationThreadMessage,
} from "./application-detail";
import { formatEnumLabel } from "./labels";

export const APPLICATION_STATUS_OPTIONS = [
  "Applied",
  "Recruiter Contact",
  "Oa Pending",
  "Interviewing",
  "Offer",
  "Rejected",
  "Closed",
];

export function toCompactStageLabel(value: string | null) {
  switch (value) {
    case "application_received":
      return "Received";
    case "recruiter_outreach":
      return "Outreach";
    case "oa_invitation":
      return "OA";
    case "interview_invitation":
      return "Interview";
    case "interview_confirmation":
      return "Confirmed";
    case "generic_update":
      return "Update";
    default:
      return formatEnumLabel(value);
  }
}

export function isTerminalStatus(status: string) {
  return status === "Rejected" || status === "Closed";
}

export function isAssessmentReminder(action: ApplicationDetailReminder) {
  return (
    action.source === "Assessment Deadline" ||
    action.title.toLowerCase().includes("assessment")
  );
}

export function isOaMessage(message: ApplicationThreadMessage) {
  return message.category === "oa_invitation";
}

function newestFirst(left: ApplicationThreadMessage, right: ApplicationThreadMessage) {
  return new Date(right.receivedAtUtc).getTime() - new Date(left.receivedAtUtc).getTime();
}

export type ReviewAgentDecision = ApplicationThreadMessage & {
  threadTitle: string;
  reviewSummary: string;
};

// The six newest messages the evidence agent scored or routed, with the thread they came from.
export function buildReviewAgentDecisions(detail: ApplicationDetail): ReviewAgentDecision[] {
  return detail.threads
    .flatMap((thread) =>
      thread.messages.map((message) => ({
        ...message,
        threadTitle: thread.title,
        reviewSummary: message.requiresManualReview
          ? message.reviewReason || "Sent to review."
          : "Updated automatically.",
      })),
    )
    .filter(
      (message) =>
        message.processingSource ||
        message.reviewReason ||
        message.classificationConfidence !== null ||
        message.matchingConfidence !== null,
    )
    .sort(newestFirst)
    .slice(0, 6);
}

export function findLatestOutboundReply(detail: ApplicationDetail) {
  return (
    detail.threads
      .flatMap((thread) => thread.messages)
      .filter((message) => message.isOutbound)
      .sort(newestFirst)[0] ?? null
  );
}

// What the page shows after "Not an OA" when the refreshed detail cannot load: the message is a
// generic update, its assessment reminders are gone, and an OA stage moves back to Applied.
export function applyNotOaCorrection(detail: ApplicationDetail, messageId: string): ApplicationDetail {
  return {
    ...detail,
    status: detail.status === "Oa Pending" ? "Applied" : detail.status,
    pendingActions: detail.pendingActions.filter((action) => !isAssessmentReminder(action)),
    threads: detail.threads.map((thread) => ({
      ...thread,
      messages: thread.messages.map((message) =>
        message.id === messageId
          ? { ...message, category: "generic_update", actionRequired: false, dueDateUtc: null }
          : message,
      ),
    })),
  };
}

import { describe, expect, it } from "vitest";
import type { ApplicationDetail, ApplicationThreadMessage } from "./application-detail";
import { applyNotOaCorrection, buildReviewAgentDecisions } from "./application-detail-view";

function message(id: string, overrides: Partial<ApplicationThreadMessage> = {}) {
  return {
    id,
    subject: `Message ${id}`,
    receivedAtUtc: "2026-11-01T12:00:00Z",
    category: "generic_update",
    actionRequired: false,
    dueDateUtc: null,
    requiresManualReview: false,
    reviewReason: null,
    processingSource: null,
    classificationConfidence: null,
    matchingConfidence: null,
    isOutbound: false,
    ...overrides,
  } as ApplicationThreadMessage;
}

function detailWith(messages: ApplicationThreadMessage[], overrides: Partial<ApplicationDetail> = {}) {
  return {
    status: "Oa Pending",
    pendingActions: [
      { id: "r1", title: "Finish the coding assessment", source: "Follow Up" },
      { id: "r2", title: "Send thank-you note", source: "Follow Up" },
    ],
    threads: [{ threadId: "t1", title: "Assessment invite", messages }],
    ...overrides,
  } as ApplicationDetail;
}

describe("buildReviewAgentDecisions", () => {
  it("keeps the messages the agent scored, newest first, with a summary", () => {
    const detail = detailWith([
      message("plain"),
      message("older", { classificationConfidence: 0.9, receivedAtUtc: "2026-10-01T00:00:00Z" }),
      message("routed", { requiresManualReview: true, reviewReason: "Company unclear" }),
    ]);

    const decisions = buildReviewAgentDecisions(detail);

    expect(decisions.map((decision) => decision.id)).toEqual(["routed", "older"]);
    expect(decisions[0]).toMatchObject({ threadTitle: "Assessment invite", reviewSummary: "Company unclear" });
    expect(decisions[1].reviewSummary).toBe("Updated automatically.");
  });
});

describe("applyNotOaCorrection", () => {
  it("clears the OA message, drops assessment reminders, and moves the stage to Applied", () => {
    const detail = detailWith([
      message("oa", { category: "oa_invitation", actionRequired: true, dueDateUtc: "2026-11-08T00:00:00Z" }),
      message("other", { category: "oa_invitation" }),
    ]);

    const corrected = applyNotOaCorrection(detail, "oa");

    expect(corrected.status).toBe("Applied");
    expect(corrected.pendingActions.map((action) => action.id)).toEqual(["r2"]);
    expect(corrected.threads[0].messages[0]).toMatchObject({
      category: "generic_update",
      actionRequired: false,
      dueDateUtc: null,
    });
    expect(corrected.threads[0].messages[1].category).toBe("oa_invitation");
  });

  it("keeps a later stage", () => {
    expect(applyNotOaCorrection(detailWith([], { status: "Interviewing" }), "oa").status).toBe("Interviewing");
  });
});

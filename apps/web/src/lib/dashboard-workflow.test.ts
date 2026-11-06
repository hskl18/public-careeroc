import { describe, expect, it } from "vitest";
import type { ApplicationCard, DashboardResponse, PendingAction } from "./dashboard";
import {
  completeApplicationActionLocally,
  getApplicationUrgencyScore,
  sortApplications,
} from "./dashboard-workflow";

function card(id: string, overrides: Partial<ApplicationCard> = {}) {
  return {
    id,
    company: `Company ${id}`,
    role: "Software Engineer",
    status: "Applied",
    priority: 3,
    actionRequired: false,
    requiresReview: false,
    hasOutboundReply: false,
    lastActivityAtUtc: "2026-11-01T12:00:00Z",
    nextAction: {
      title: "Confirm application sent",
      dueAtUtc: null,
      hasExplicitDueDate: false,
      tone: "muted",
      sourceReminderId: null,
    },
    ...overrides,
  } as ApplicationCard;
}

function reminder(id: string, jobApplicationId: string) {
  return {
    id,
    jobApplicationId,
    title: "Finish the assessment",
    dueAtUtc: "2026-11-06T17:00:00Z",
    hasExplicitDueDate: true,
  } as PendingAction;
}

function dashboardWith(applications: ApplicationCard[], pendingActions: PendingAction[], needsAction: number) {
  return {
    metrics: { needsAction },
    applications,
    pendingActions,
  } as DashboardResponse;
}

describe("sortApplications", () => {
  it("puts action required first, then the higher urgency score", () => {
    const quiet = card("quiet", { urgencyScore: 90 });
    const urgent = card("urgent", { urgencyScore: 10, actionRequired: true });
    const loud = card("loud", { urgencyScore: 50, actionRequired: true });

    const order = sortApplications([quiet, urgent, loud], { column: null, direction: "desc" });

    expect(order.map((application) => application.id)).toEqual(["loud", "urgent", "quiet"]);
  });

  it("sorts by the chosen column and breaks ties by recent activity", () => {
    const older = card("older", { company: "Acme", lastActivityAtUtc: "2026-10-01T00:00:00Z" });
    const newer = card("newer", { company: "acme", lastActivityAtUtc: "2026-11-02T00:00:00Z" });
    const other = card("other", { company: "Beta" });

    const order = sortApplications([other, older, newer], { column: "company", direction: "asc" });

    expect(order.map((application) => application.id)).toEqual(["newer", "older", "other"]);
  });

  it("ranks next actions by tone, with an unknown tone last", () => {
    const waiting = card("waiting", { nextAction: { ...card("x").nextAction, tone: "waiting" } });
    const action = card("action", { nextAction: { ...card("x").nextAction, tone: "action" } });
    const unknown = card("unknown", {
      nextAction: { ...card("x").nextAction, tone: "unexpected" as ApplicationCard["nextAction"]["tone"] },
    });

    const order = sortApplications([unknown, waiting, action], { column: "next-action", direction: "desc" });

    expect(order.map((application) => application.id)).toEqual(["action", "waiting", "unknown"]);
  });
});

describe("getApplicationUrgencyScore", () => {
  const nowMs = Date.parse("2026-11-05T12:00:00Z");

  it("uses the API's score when it is present", () => {
    expect(getApplicationUrgencyScore(card("a", { urgencyScore: 7 }), nowMs)).toBe(7);
  });

  it("scores a due-tomorrow assessment that needs action", () => {
    const application = card("a", {
      urgencyScore: undefined,
      status: "Oa Pending",
      actionRequired: true,
      nextActionDueAtUtc: "2026-11-06T09:00:00Z",
    });

    expect(getApplicationUrgencyScore(application, nowMs)).toBe(100 + 40 + 20);
  });
});

describe("completeApplicationActionLocally", () => {
  it("drops the reminder, moves an assessment back to Applied, and lowers Needs action", () => {
    const application = card("a", { status: "Oa Pending", actionRequired: true });
    const dashboard = dashboardWith([application], [reminder("r1", "a")], 1);

    const next = completeApplicationActionLocally(
      dashboard,
      { applicationId: "a", reminderId: "r1", completesStage: true },
      "2026-11-05T12:00:00Z",
    );

    expect(next.pendingActions).toEqual([]);
    expect(next.metrics.needsAction).toBe(0);
    expect(next.applications[0]).toMatchObject({
      status: "Applied",
      actionRequired: false,
      lastActivityAtUtc: "2026-11-05T12:00:00Z",
      nextAction: { title: "Confirm application sent", tone: "muted" },
    });
  });

  it("keeps Needs action when another reminder remains for the application", () => {
    const application = card("a", { actionRequired: true });
    const dashboard = dashboardWith([application], [reminder("r1", "a"), reminder("r2", "a")], 1);

    const next = completeApplicationActionLocally(dashboard, {
      applicationId: "a",
      reminderId: "r1",
      completesStage: true,
    });

    expect(next.metrics.needsAction).toBe(1);
    expect(next.applications[0].nextAction).toMatchObject({ sourceReminderId: "r2", tone: "action" });
  });

  it("keeps an assessment stage when another task on the application is done", () => {
    const application = card("a", { status: "Oa Pending", actionRequired: true });
    const dashboard = dashboardWith([application], [reminder("r1", "a"), reminder("r2", "a")], 1);

    const next = completeApplicationActionLocally(dashboard, {
      applicationId: "a",
      reminderId: "r2",
      completesStage: false,
    });

    expect(next.applications[0].status).toBe("Oa Pending");
  });
});


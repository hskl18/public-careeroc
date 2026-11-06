import { expect, test } from "@playwright/test";

const requiresHarness = process.env.CAREEROC_MULTI_AGENT_E2E !== "1";
const fixtureUrl = process.env.CAREEROC_E2E_AGENT_FIXTURE_URL ?? "http://127.0.0.1:8123";

test.use({
  trace: "retain-on-failure",
  video: "retain-on-failure",
});

test.describe("multi-agent resume and mailbox review", () => {
  test("runs the signed full-stack flow and persists the gated agent trace", async ({
    page,
    request,
  }) => {
    test.skip(requiresHarness, "Run with `pnpm e2e:multi-agent` from the repository root.");

    const consoleErrors: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") {
        consoleErrors.push(message.text());
      }
    });

    const reviewResponse = page.waitForResponse(
      (response) =>
        response.request().method() === "POST" &&
        response.url().endsWith("/api/orchestrator/resume-mailbox-review"),
    );

    await page.goto("/app");
    await expect(page).toHaveURL(/\/app$/);
    // The review is one row on the dashboard; running and deciding happen in its dialog.
    const openReview = () => page.getByRole("button", { name: /Multi-agent review/ }).click();
    const dialog = page.getByRole("dialog", { name: "Multi-agent review" });
    await openReview();

    await dialog.getByRole("button", { name: "Run review" }).click();

    const response = await reviewResponse;
    expect(response.status()).toBe(200);
    const payload = (await response.json()) as {
      runtimePath: string;
      modelPath: string;
      status: string;
      requiresHumanReview: boolean;
      confidence: number;
      threadId: string;
      trace: { steps: Array<{ agentName: string }> };
    };
    expect(payload).toMatchObject({
      runtimePath: "langgraph.resume_mailbox_review.v1",
      modelPath: "fixture/e2e-structured-output",
      status: "review_required",
      requiresHumanReview: true,
      confidence: 0.88,
    });
    expect(payload.threadId).toMatch(/^resume-mailbox:[a-f0-9]{32}$/);
    expect(payload.trace.steps.map((step) => step.agentName)).toEqual([
      "resume_analyst",
      "mailbox_analyst",
      "strategy_analyst",
      "career_supervisor",
    ]);

    await expect(dialog.getByText("Review updated.", { exact: true }).first()).toBeVisible();
    await expect(dialog).toContainText("fixture/e2e-structured-output · confidence 88%");
    await expect(dialog.getByText("Waiting for your approval")).toBeVisible();
    await expect(dialog.getByText("resume analyst", { exact: true })).toBeVisible();
    await expect(dialog.getByText("mailbox analyst", { exact: true })).toBeVisible();
    await expect(dialog.getByText("strategy analyst", { exact: true })).toBeVisible();
    await expect(dialog.getByText("career supervisor", { exact: true })).toBeVisible();

    const fixtureStateResponse = await request.get(`${fixtureUrl}/__e2e/state`);
    expect(fixtureStateResponse.ok()).toBe(true);
    const fixtureState = (await fixtureStateResponse.json()) as {
      acceptedRequestCount: number;
      lastRequest: Record<string, unknown> & {
        threadId: string;
        baselineSummary: string;
        evidence: Record<string, number>;
      };
    };
    expect(fixtureState.acceptedRequestCount).toBe(1);
    expect(fixtureState.lastRequest.threadId).toBe(payload.threadId);
    expect(fixtureState.lastRequest.baselineSummary).toBeTruthy();
    expect(fixtureState.lastRequest.evidence).toEqual(
      expect.objectContaining({
        resumeVersions: expect.any(Number),
        totalApplications: expect.any(Number),
      }),
    );
    expect(fixtureState.lastRequest).not.toHaveProperty("rawEmailBodies");
    expect(fixtureState.lastRequest).not.toHaveProperty("emailBodies");

    await page.reload();
    await expect(page.getByText("Needs your approval", { exact: true })).toBeVisible();
    await openReview();
    await expect(dialog).toContainText("fixture/e2e-structured-output");
    await expect(dialog.getByText("Waiting for your approval")).toBeVisible();
    await expect(dialog.getByText("career supervisor", { exact: true })).toBeVisible();

    const hasHorizontalOverflow = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
    expect(hasHorizontalOverflow).toBe(false);
    expect(consoleErrors).toEqual([]);
    await page.screenshot({
      fullPage: true,
      path: "../../.artifacts/e2e/multi-agent/browser-success.png",
    });
  });
});

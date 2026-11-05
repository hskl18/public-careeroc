import { expect, test } from "@playwright/test";

// The hosted demo runs with DEMO_SNAPSHOT and no backend, a path the full-stack jobs never take.
test.describe("hosted read-only demo", () => {
  test.skip(
    process.env.CAREEROC_HOSTED_DEMO_E2E !== "1",
    "Needs a production build served with DEMO_MODE and DEMO_SNAPSHOT.",
  );

  test("an anonymous request gets no workspace data", async ({ request }) => {
    for (const route of ["/app", "/app/applications", "/app/resume"]) {
      const body = await (await request.get(route, { maxRedirects: 0 })).text();
      expect(body).not.toContain("Kestrel AI");
      expect(body).not.toContain("alex.rivera");
    }
  });

  test("a visitor can open the demo and walk through the recorded review", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(String(error)));
    page.on("console", (message) => {
      if (message.type() === "error") errors.push(message.text());
    });

    await page.goto("/");
    await page.getByRole("button", { name: "Open the demo" }).click();
    await page.waitForURL("**/app");

    await expect(page.getByText("Multi-agent review", { exact: true })).toBeVisible();
    await expect(page.getByText("Recorded run", { exact: true })).toBeVisible();
    await expect(page.getByText(/^ollama\//)).toBeVisible();
    await expect(page.getByText("deterministic evidence gate", { exact: true })).toBeVisible();
    await expect(page.getByText("Waiting for your approval")).toBeVisible();

    // Approval replays locally; the demo must not attempt a write.
    const writes: string[] = [];
    page.on("request", (request) => {
      if (request.method() !== "GET" && request.url().includes("/api/careeroc/")) {
        const path = new URL(request.url()).pathname;
        if (!path.endsWith("/dashboard/inbox-check")) writes.push(path);
      }
    });
    await page.getByRole("button", { name: "Approve" }).click();
    await expect(page.getByText("human reviewer", { exact: true })).toBeVisible();
    await expect(page.getByText("Replayed in this read-only demo. Nothing was saved.")).toBeVisible();
    expect(writes).toEqual([]);

    await page.reload();
    await expect(page.getByText("Waiting for your approval")).toBeVisible();

    await page.goto("/app/inbox");
    await expect(page.getByText("Demo inbox is connected")).toBeVisible();
    await expect(page.getByText("No Gmail inbox connected yet")).toHaveCount(0);

    for (const route of [
      "/app/applications",
      "/app/analytics",
      "/app/resume",
      "/app/review",
      "/app/memory",
      "/app/inbox",
    ]) {
      await page.goto(route);
      await expect(page.getByRole("heading").first()).toBeVisible();
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      expect(overflow, route).toBeLessThanOrEqual(0);
    }

    expect(errors).toEqual([]);
  });

  test("the proxy refuses writes", async ({ page, request }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Open the demo" }).click();
    await page.waitForURL("**/app");
    const origin = new URL(page.url()).origin;
    const response = await page.context().request.post("/api/careeroc/api/orchestrator/resume-mailbox-review", {
      headers: { origin },
    });
    expect(response.status()).toBe(403);
    expect((await request.get("/tech")).status()).toBe(200);
  });
});

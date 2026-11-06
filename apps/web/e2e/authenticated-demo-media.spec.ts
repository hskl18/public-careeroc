import { expect, test } from "@playwright/test";

const requiresAuth = !process.env.PLAYWRIGHT_STORAGE_STATE;
const skipReason =
  "Set PLAYWRIGHT_STORAGE_STATE=e2e/.auth/user.json after signing in with the demo account.";

const screenshotRoutes = [
  {
    path: "/app",
    name: "app-dashboard",
    readyText: /connect gmail|needs action|recent activity/i,
  },
  {
    path: "/app/inbox",
    name: "app-inbox-contacts",
    readyText: /contacts|connect gmail|gmail inbox|demo inbox/i,
    focusText: /contacts/i,
  },
  {
    path: "/app/applications",
    name: "app-applications",
    readyText: /needs action/i,
  },
  {
    path: "/app/memory",
    name: "app-memory",
    readyText: /pending|confirmed/i,
  },
] as const;

test.describe("authenticated demo media", () => {
  for (const route of screenshotRoutes) {
    test(`${route.name} screenshot`, async ({ page }, testInfo) => {
      test.skip(requiresAuth, skipReason);

      await page.goto(route.path);
      await expect(page).toHaveURL(new RegExp(`${route.path.replace("/", "\\/")}(\\?.*)?$`));
      await expect(page.getByRole("main")).toBeVisible();
      await expect(page.getByText(route.readyText).first()).toBeVisible({
        timeout: 15_000,
      });
      if ("focusText" in route) {
        await page.getByText(route.focusText).first().scrollIntoViewIfNeeded();
      }
      await page.screenshot({
        fullPage: true,
        path: `../../.artifacts/demo-media/${route.name}-${testInfo.project.name}.png`,
      });
    });
  }
});

import { expect, test } from "@playwright/test";

const requiresAuth = !process.env.PLAYWRIGHT_STORAGE_STATE;
const skipReason =
  "Run `pnpm e2e:auth` from apps/web, sign in, then set PLAYWRIGHT_STORAGE_STATE=e2e/.auth/user.json.";

test.describe("cold-start UX", () => {
  test("dashboard shows onboarding hero and working connect-inbox fragment link", async ({
    page,
  }) => {
    test.skip(requiresAuth, skipReason);

    await page.goto("/app");

    const hero = page
      .getByRole("heading", {
        name: /connect gmail|first sync|analyzing/i,
      })
      .first();
    await expect(hero).toBeVisible();

    const setupCta = page
      .getByRole("link", { name: /connect gmail|set up gmail/i })
      .first();
    await expect(setupCta).toBeVisible();

    await setupCta.click();
    await expect(page).toHaveURL(/\/app\/inbox#connect-inbox$/);
    await expect(page.locator("#connect-inbox")).toBeVisible();
  });

  test("header sync pill deep-links based on connection state", async ({
    page,
  }) => {
    test.skip(requiresAuth, skipReason);

    await page.goto("/app");

    const pill = page
      .getByRole("link")
      .filter({ hasText: /connect gmail|waiting|analyzing|synced|api offline/i })
      .first();
    await expect(pill).toBeVisible();

    const href = await pill.getAttribute("href");
    expect(href).toMatch(/\/app\/inbox#(connect-inbox|mail-sync)$/);
  });

  test("review route opens the manual review queue", async ({ page }) => {
    test.skip(requiresAuth, skipReason);

    await page.goto("/app/review");
    await expect(page).toHaveURL(/\/app\/review$/);
    await expect(page.getByRole("heading", { name: /review/i })).toBeVisible();
  });

  test("home page loads without auth", async ({ page }) => {
    await page.goto("/");
    await expect(
      page.getByRole("heading", { name: /recruiting email.*application pipeline/i }),
    ).toBeVisible();
  });
});

import { expect, test } from "@playwright/test";

test("home page renders in the local browser", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("body")).toBeVisible();
});

test("applications page has no horizontal overflow or clipped buttons", async ({
  page,
}, testInfo) => {
  test.skip(
    !process.env.PLAYWRIGHT_STORAGE_STATE,
    "Run `pnpm e2e:auth` from apps/web, sign in, then set PLAYWRIGHT_STORAGE_STATE=e2e/.auth/user.json.",
  );

  await page.goto("/app/applications?focus=needs-action");
  await expect(
    page.getByRole("heading", {
      name: /job pipeline/i,
    }),
  ).toBeVisible();

  const layoutIssues = await page.evaluate(() => {
    const viewportWidth = document.documentElement.clientWidth;
    const offscreenElements = Array.from(document.querySelectorAll("body *"))
      .map((element) => {
        const rect = element.getBoundingClientRect();
        const style = window.getComputedStyle(element);

        if (
          rect.width <= 0 ||
          rect.height <= 0 ||
          style.position === "fixed" ||
          rect.right <= viewportWidth + 1
        ) {
          return null;
        }

        return {
          className: element.getAttribute("class") ?? "",
          right: Math.round(rect.right),
          tagName: element.tagName.toLowerCase(),
          text: (element.textContent ?? "").trim().slice(0, 80),
        };
      })
      .filter(Boolean)
      .slice(0, 10);

    const clippedButtons = Array.from(document.querySelectorAll(".btn"))
      .filter(
        (element) =>
          element.scrollWidth > element.clientWidth + 1 ||
          element.scrollHeight > element.clientHeight + 1,
      )
      .map((element) => ({
        className: element.getAttribute("class") ?? "",
        clientHeight: element.clientHeight,
        clientWidth: element.clientWidth,
        scrollHeight: element.scrollHeight,
        scrollWidth: element.scrollWidth,
        text: (element.textContent ?? "").trim(),
      }));

    return {
      clippedButtons,
      offscreenElements,
    };
  });

  expect(layoutIssues.offscreenElements).toEqual([]);
  expect(layoutIssues.clippedButtons).toEqual([]);

  await page.screenshot({
    fullPage: true,
    path: `test-results/applications-needs-action-${testInfo.project.name}.png`,
  });
});

import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

test.describe("basic accessibility smoke", () => {
  test("public pages have no obvious axe violations", async ({ page }) => {
    for (const route of ["/", "/tech"]) {
      await page.goto(route);

      await expect(page.locator("h1")).toHaveCount(1);

      const unlabeledButtons = await page.evaluate(() =>
        Array.from(document.querySelectorAll("button"))
          .filter((button) => {
            const label =
              button.getAttribute("aria-label") ||
              button.getAttribute("title") ||
              button.textContent;
            return !label?.trim();
          })
          .map((button) => button.outerHTML.slice(0, 160)),
      );

      expect(unlabeledButtons).toEqual([]);

      const results = await new AxeBuilder({ page })
        .disableRules(["color-contrast", "page-has-heading-one"])
        .analyze();

      expect(results.violations).toEqual([]);
    }
  });
});

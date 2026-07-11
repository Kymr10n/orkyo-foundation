import { test, expect } from "@playwright/test";

/**
 * Review screenshots for the redesigned Requests components (tree/list),
 * rendered against fixture data in e2e/harness. Chromium only — this is
 * a design-review aid, not a cross-engine assertion suite (see smoke.spec.ts).
 * NOT wired into CI; run on demand: `npx playwright test e2e/requests-visual.spec.ts`.
 * Output lands in e2e/review/ (gitignored).
 */

test.describe("Requests visual review", () => {
  test("captures light and dark screenshots", async ({ page }) => {
    await page.goto("/");
    const section = page.getByTestId("requests-section");
    await expect(section).toBeVisible();
    // Wait for the fixture tree to paint before the first capture.
    const tree = page.getByTestId("requests-tree");
    await expect(tree.getByText("Conference Setup")).toBeVisible();
    await expect(tree.getByText("Catering Delivery")).toBeVisible();

    await page.screenshot({ path: "e2e/review/requests-light.png", fullPage: true });
    await page.getByTestId("requests-tree").screenshot({ path: "e2e/review/tree-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    // Let the theme change settle before capturing.
    await expect(page.locator("html")).toHaveClass(/dark/);

    await page.screenshot({ path: "e2e/review/requests-dark.png", fullPage: true });
    await page.getByTestId("requests-tree").screenshot({ path: "e2e/review/tree-dark.png" });
  });

  test("tree row inline actions, light and dark", async ({ page }) => {
    await page.goto("/");
    const tree = page.getByTestId("requests-tree");
    await expect(tree.getByText("Catering Delivery")).toBeVisible();

    // The per-row kebab menu was replaced by inline Edit/Delete ghost icon
    // buttons. In the tree they're hover-revealed on desktop
    // (opacity-0 group-hover:opacity-100), so hover the row first.
    const row = tree.getByRole("treeitem", { name: /Catering Delivery/ });
    await row.hover();
    const editButton = row.getByRole("button", { name: "Edit Catering Delivery" });
    const deleteButton = row.getByRole("button", { name: "Delete Catering Delivery" });
    await expect(editButton).toBeVisible();
    await expect(deleteButton).toBeVisible();

    await page.screenshot({ path: "e2e/review/tree-row-actions-light.png", fullPage: true });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await row.hover();
    await expect(editButton).toBeVisible();
    await expect(deleteButton).toBeVisible();
    await page.screenshot({ path: "e2e/review/tree-row-actions-dark.png", fullPage: true });
  });

  test("list row inline actions, light and dark", async ({ page }) => {
    await page.goto("/");
    const list = page.getByTestId("requests-list");
    await expect(list).toBeVisible();

    // The list view's Edit/Delete buttons are persistent (not hover-revealed).
    const editButton = list.getByRole("button", { name: /^Edit / }).first();
    const deleteButton = list.getByRole("button", { name: /^Delete / }).first();
    await expect(editButton).toBeVisible();
    await expect(deleteButton).toBeVisible();

    await list.screenshot({ path: "e2e/review/list-row-actions-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await expect(editButton).toBeVisible();
    await expect(deleteButton).toBeVisible();
    await list.screenshot({ path: "e2e/review/list-row-actions-dark.png" });
  });
});

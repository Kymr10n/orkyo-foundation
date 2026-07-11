import { test, expect } from "@playwright/test";

/**
 * Review screenshots for the redesigned RequestFormDialog (single view+edit
 * surface for Requests), rendered against fixture data in e2e/harness.
 * Chromium only — this is a design-review aid, not a cross-engine assertion
 * suite (see smoke.spec.ts). NOT wired into CI; run on demand:
 * `npx playwright test e2e/requests-dialog-visual.spec.ts --project=chromium`.
 * Output lands in e2e/review/ (gitignored).
 */

test.describe("RequestFormDialog visual review", () => {
  test("group (edit) — Children tab, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();
    await expect(dialog.getByText("Rig Truss")).toBeVisible();
    await expect(dialog.getByText("Sound Check")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/dialog-group-children-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/dialog-group-children-dark.png" });
  });

  test("leaf (view) — Resources tab shows resolved names, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-leaf").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Resources" }).click();

    // Resolved names, not raw UUIDs — this is the whole point of the review.
    await expect(dialog).toContainText("Bay 3");
    await expect(dialog).toContainText("Alex Welder");
    await expect(dialog).toContainText("Sam Fabricator");
    await expect(dialog).not.toContainText("person-alex");
    await expect(dialog).not.toContainText("person-sam");
    await expect(dialog).not.toContainText("space-bay3");

    await dialog.screenshot({ path: "e2e/review/dialog-leaf-resources-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/dialog-leaf-resources-dark.png" });
  });

  test("create (group) — Timing tab derivation placeholder, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Timing" }).click();
    await expect(
      dialog.getByText("Summary dates and duration are automatically calculated from child requests."),
    ).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-group-timing-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-group-timing-dark.png" });
  });

  test("create (group) — Children tab quick-add, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();

    const nameInput = dialog.getByTestId("new-child-name");
    const addBtn = dialog.getByTestId("add-child-btn");

    await nameInput.fill("Rig Truss");
    await addBtn.click();
    await expect(dialog.getByText("Rig Truss")).toBeVisible();

    await nameInput.fill("Sound Check");
    await addBtn.click();
    await expect(dialog.getByText("Sound Check")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-group-children-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-group-children-dark.png" });
  });

  test("create (group) — Timing tab, boundary mode off, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();

    // Boundary mode is off by default (planningMode 'summary') — just confirm
    // the checkbox is unchecked before switching to Timing.
    await dialog.getByRole("tab", { name: "Details" }).click();
    await expect(dialog.locator("#group-boundary-mode")).not.toBeChecked();

    await dialog.getByRole("tab", { name: "Timing" }).click();
    await expect(
      dialog.getByText("Summary dates and duration are automatically calculated from child requests."),
    ).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-group-timing-boundary-off-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-group-timing-boundary-off-dark.png" });
  });

  test("create (group) — Timing tab, boundary mode on, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();

    await dialog.getByRole("tab", { name: "Details" }).click();
    await dialog.locator("#group-boundary-mode").check();
    await expect(dialog.locator("#group-boundary-mode")).toBeChecked();

    await dialog.getByRole("tab", { name: "Timing" }).click();
    await expect(
      dialog.getByText(
        "Dates and duration roll up from children. The boundary window below limits when they can be scheduled.",
      ),
    ).toBeVisible();
    await expect(dialog.getByText("Boundary Window (Optional)")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-group-timing-boundary-on-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-group-timing-boundary-on-dark.png" });
  });

  test("group (edit) — Children tab, add existing panel, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();

    await dialog.getByTestId("add-existing-toggle").click();
    await expect(dialog.getByTestId("add-existing-confirm")).toBeVisible();
    await expect(dialog.getByText("Loose Task A")).toBeVisible();
    await expect(dialog.getByText("Loose Group B")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/children-add-existing-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/children-add-existing-dark.png" });
  });

  test("create (group) — Children tab, add existing panel expanded, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();

    await dialog.getByTestId("add-existing-toggle").click();
    await expect(dialog.getByTestId("add-existing-confirm")).toBeVisible();
    await expect(dialog.getByText("Loose Task A")).toBeVisible();
    await expect(dialog.getByText("Loose Group B")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-add-existing-panel-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-add-existing-panel-dark.png" });
  });

  test("create (group) — Children tab, add existing queued alongside new child, light and dark", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-create-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();

    await dialog.getByTestId("add-existing-toggle").click();
    await dialog.getByRole("checkbox", { name: /Loose Task A/ }).click();
    await dialog.getByTestId("add-existing-confirm").click();
    await expect(dialog.getByRole("button", { name: "Remove Loose Task A" })).toBeVisible();

    const nameInput = dialog.getByTestId("new-child-name");
    const addBtn = dialog.getByTestId("add-child-btn");
    await nameInput.fill("Rig Truss");
    await addBtn.click();
    await expect(dialog.getByText("Rig Truss")).toBeVisible();

    await dialog.screenshot({ path: "e2e/review/create-add-existing-queued-light.png" });

    await page.evaluate(() => document.documentElement.classList.add("dark"));
    await expect(page.locator("html")).toHaveClass(/dark/);
    await dialog.screenshot({ path: "e2e/review/create-add-existing-queued-dark.png" });
  });

  test("group (edit) — Children tab, add existing row click toggles checkbox without freezing", async ({ page }) => {
    // Regression check for the "Add existing" candidate row, which is a
    // <div onClick={toggle}> (not a <label>) wrapping a Radix Checkbox with
    // onClick stopPropagation — see RequestFormDialog.tsx. In jsdom (vitest)
    // clicking the row text triggered a "Maximum update depth exceeded" loop
    // via Radix ScrollArea's ref; this test confirms whether that reproduces
    // in a real browser.
    await page.goto("/");
    await page.getByTestId("open-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("tab", { name: "Children" }).click();

    await dialog.getByTestId("add-existing-toggle").click();
    const row = dialog.getByText("Loose Task A");
    await expect(row).toBeVisible();

    const checkbox = dialog.getByRole("checkbox", { name: "Loose Task A" });
    await expect(checkbox).toHaveAttribute("aria-checked", "false");

    // Click the row TEXT (not the checkbox) — should toggle checked via the
    // div's onClick.
    await row.click();
    await expect(checkbox).toHaveAttribute("aria-checked", "true");

    // Click the checkbox itself — stopPropagation means the row's onClick
    // must not also fire, so a single click toggles exactly once (back to
    // unchecked, not a double-toggle no-op).
    await checkbox.click();
    await expect(checkbox).toHaveAttribute("aria-checked", "false");

    // Click the row text again — checked again, and the page must still be
    // responsive (this is what catches a freeze/infinite render loop).
    await row.click();
    await expect(checkbox).toHaveAttribute("aria-checked", "true");
    await expect(dialog.getByLabel("Search requests to add")).toBeVisible();
  });
});

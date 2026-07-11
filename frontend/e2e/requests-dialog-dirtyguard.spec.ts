import { test, expect } from "@playwright/test";

/**
 * Regression test for the dirty-guard "Keep editing" loop bug in
 * RequestFormDialog. Previously, closing the confirm via "Keep editing"
 * re-triggered the discard prompt (via a trailing outside-interaction event
 * on the underlying dialog), trapping the user — the only escape was to
 * discard. The fix (useDialogDirtyGuard + onInteractOutside/onEscapeKeyDown
 * guarding on `isDirty`) makes "Keep editing" return the user to the
 * still-open dialog exactly once, with no re-trigger.
 *
 * Chromium only — behavioral regression check, not a cross-engine suite.
 * Run: `npx playwright test e2e/requests-dialog-dirtyguard.spec.ts --project=chromium`.
 */

test.describe("RequestFormDialog dirty guard", () => {
  test("Keep editing dismisses the confirm exactly once and does not loop", async ({ page }) => {
    await page.goto("/");
    await page.getByTestId("open-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText("Edit Request")).toBeVisible();

    // Details tab is the default; make the form dirty by editing the Name field.
    const nameInput = dialog.locator("#name");
    await expect(nameInput).toBeVisible();
    await nameInput.fill("Modified Name");

    // Attempt to close via the dialog's X button.
    await dialog.getByRole("button", { name: "Close" }).click();

    const confirmDialog = page.getByRole("alertdialog");
    await expect(confirmDialog).toBeVisible();
    await expect(confirmDialog.getByText("Discard changes?")).toBeVisible();

    // Click "Keep editing" — this must dismiss the confirm exactly once,
    // not immediately re-trigger it.
    await confirmDialog.getByRole("button", { name: "Keep editing" }).click();

    await expect(confirmDialog).not.toBeVisible();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText("Edit Request")).toBeVisible();
    await expect(nameInput).toHaveValue("Modified Name");

    // Anti-loop assertion: wait and confirm the prompt did not reappear on
    // its own, and the main dialog is still open.
    await page.waitForTimeout(500);
    await expect(page.getByRole("alertdialog")).not.toBeVisible();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText("Edit Request")).toBeVisible();

    // Sanity: the intended close path (Discard changes) still works.
    await dialog.getByRole("button", { name: "Close" }).click();
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await page.getByRole("alertdialog").getByRole("button", { name: "Discard changes" }).click();
    await expect(page.getByRole("alertdialog")).not.toBeVisible();
    await expect(page.getByText("Edit Request")).not.toBeVisible();
  });

  test("interacting with the Children tab only does not dirty the form and closes without prompting", async ({
    page,
  }) => {
    await page.goto("/");
    await page.getByTestId("open-group").click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText("Edit Request")).toBeVisible();

    // Go to the Children tab and interact with the quick-add input only —
    // per RequestFormDialog, Children-tab controls stopPropagation() on
    // input/change so they never bubble to the form-level setIsDirty(true).
    await dialog.getByRole("tab", { name: "Children" }).click();
    await dialog.getByTestId("new-child-name").fill("Some child");

    // Close via the dialog's X button — with no real field dirtied, this
    // should close immediately with no "Discard changes?" prompt.
    await dialog.getByRole("button", { name: "Close" }).click();

    await expect(page.getByText("Discard changes?")).toHaveCount(0);
    await expect(page.getByRole("alertdialog")).not.toBeVisible();
    await expect(page.getByText("Edit Request")).not.toBeVisible();
  });
});

import { test, expect } from "@playwright/test";

/**
 * Mobile/tablet viewport suite (WP6). Runs ONLY on the mobile-webkit (iPhone 14),
 * mobile-chromium (Pixel 7) and tablet-webkit (iPad gen 7) projects — see
 * playwright.config.ts. On-demand only, never a PR CI gate (e2e/README.md).
 *
 * These assert the phone/tablet responsiveness behaviours the vitest suite can't
 * see because they depend on a real viewport + matchMedia:
 *   1. OrkyoDataTable renders cards (no <table>) on phone, a real <table> on tablet.
 *   2. FormDialog keeps its submit footer inside the viewport (dvh cap) and Enter submits.
 *   3. The wizard tab strip scrolls horizontally so the last tab is reachable + clickable.
 *   4. RequestCalendar opens on the compressed month grid on phone.
 *
 * The TopBar phone-overflow check (plan WP6 item 4) is intentionally omitted here:
 * the fixture-only harness doesn't mount TopBar, and stubbing its AuthContext /
 * useSites / router surface would need several mock modules. The overflow menu is
 * covered by the TopBar vitest instead (src/components/layout/TopBar.test.tsx).
 */

/** iPad project is the only tablet-tier project; the other two are phones. */
function isTablet(projectName: string): boolean {
  return projectName.startsWith("tablet");
}

test("OrkyoDataTable: phone shows cards (no table), tablet shows a table", async ({
  page,
}, testInfo) => {
  await page.goto("/");
  const table = page.getByTestId("data-table");

  if (isTablet(testInfo.project.name)) {
    await expect(table.locator("table")).toBeVisible();
    return;
  }

  // Phone: card mode — the primary identifier renders in a card, no <table> element.
  await expect(table.getByTestId("card-row-1")).toBeVisible();
  await expect(table.locator("table")).toHaveCount(0);

  // Pagination still works in card mode.
  await table.getByRole("button", { name: "Next page" }).click();
  await expect(table.getByTestId("card-row-3")).toBeVisible();
  await expect(table.getByTestId("card-row-1")).toHaveCount(0);
});

test("FormDialog: submit footer stays within the viewport and Enter submits", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByTestId("open-form").click();

  const input = page.getByTestId("task-name");
  await expect(input).toBeVisible();

  // The dvh-capped dialog must keep the submit button on-screen (footer reachable).
  const submit = page.getByRole("button", { name: "Save" });
  const box = await submit.boundingBox();
  const viewport = page.viewportSize();
  expect(box).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(box!.y + box!.height).toBeLessThanOrEqual(viewport!.height + 1);

  await input.fill("Buy milk");
  await input.press("Enter");
  await expect(page.getByTestId("form-result")).toHaveText("Submitted: Buy milk");
});

test("Wizard tab strip scrolls horizontally to reach the last tab", async ({ page }) => {
  await page.goto("/");

  const strip = page.getByTestId("wizard-tabs-strip");
  // The strip overflows its container (horizontal scroll available).
  const overflows = await strip.evaluate((el) => el.scrollWidth > el.clientWidth);
  expect(overflows).toBe(true);

  const lastTab = page.getByTestId("wizard-tab-last");
  await lastTab.scrollIntoViewIfNeeded();
  await lastTab.click();
  await expect(page.getByTestId("wizard-content-last")).toBeVisible();
});

test("RequestCalendar opens on the compressed month grid on phone", async ({
  page,
}, testInfo) => {
  test.skip(isTablet(testInfo.project.name), "tablet keeps the time-grid views");
  await page.goto("/");

  const calendar = page.getByTestId("calendar");
  // Phone forces dayGridMonth even though the harness passes initialView="timeGridWeek".
  await expect(calendar.locator(".fc-daygrid").first()).toBeVisible();
  await expect(calendar.locator(".fc-timegrid")).toHaveCount(0);
});

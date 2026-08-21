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
 *   4. The page-chrome skeleton stays within the phone density budget (UI-GUIDELINES §16).
 *   5. The resource form takes the whole phone screen, never scrolls sideways, and gives
 *      every field the same control height.
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

test("Page chrome stays within the phone density budget", async ({ page }, testInfo) => {
  // UI-GUIDELINES §16: on phones the chrome above tab content — PageLayout p-3
  // (12) + title row (h-9 actions → 36) + header mb-2 (8) + tab strip h-9 (36)
  // + mb-2 (8) = 100px. Budget 104 leaves 4px slack for sub-pixel rounding.
  // Regression guard for the doubled-padding / mb-4-everywhere layout this
  // replaced (which measured ~148px for the same skeleton).
  test.skip(isTablet(testInfo.project.name), "budget is phone-only; md: metrics are unchanged");

  await page.goto("/");
  const layout = await page.getByTestId("density-layout").boundingBox();
  const content = await page.getByTestId("density-content").boundingBox();
  expect(layout).not.toBeNull();
  expect(content).not.toBeNull();
  expect(content!.y - layout!.y).toBeLessThanOrEqual(104);
});

test("Wizard tab strip scrolls horizontally to reach the last tab", async ({ page }) => {
  await page.goto("/");

  // The scroll owner is TabsList's internal wrapper, not the tablist element itself
  // (which is `w-max`, i.e. exactly as wide as its tabs and therefore never scrollable).
  const strip = page
    .getByTestId("wizard-tabs-section")
    .locator('[data-slot="tabs-list-scroller"]');
  // The strip overflows its container (horizontal scroll available).
  const overflows = await strip.evaluate((el) => el.scrollWidth > el.clientWidth);
  expect(overflows).toBe(true);

  const lastTab = page.getByTestId("wizard-tab-last");
  await lastTab.scrollIntoViewIfNeeded();
  await lastTab.click();
  await expect(page.getByTestId("wizard-content-last")).toBeVisible();
});

test("Resource form fills the phone screen and never scrolls sideways", async ({
  page,
}, testInfo) => {
  test.skip(
    isTablet(testInfo.project.name),
    "full-screen takeover is phone-only; the tablet card is unchanged",
  );

  await page.goto("/");
  // The section sits below tall fixtures, so the click can be intercepted mid-scroll.
  await page.getByTestId("open-resource").click({ force: true });
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  const viewport = page.viewportSize()!;
  const box = (await dialog.boundingBox())!;
  // Edge-to-edge and full height: the form used to float as an 85dvh band with dead
  // space above and below, which is what "does not use the mobile screen" looked like.
  expect(box.x).toBeLessThanOrEqual(1);
  expect(box.width).toBeGreaterThanOrEqual(viewport.width - 1);
  expect(box.height).toBeGreaterThanOrEqual(viewport.height - 1);

  // No sideways scroll anywhere in the dialog — that is what pushed the field labels
  // off the left edge while the pinned footer stayed put. Elements narrower than a few
  // pixels are the sr-only clip boxes (the close button's label), not visible content.
  const bleeding = await dialog.evaluate((el) =>
    [el, ...el.querySelectorAll("*")]
      .filter((n) => n.clientWidth > 4)
      .filter((n) => n.scrollWidth > n.clientWidth + 1)
      .map((n) => `${n.tagName.toLowerCase()}.${n.className}`),
  );
  expect(bleeding).toEqual([]);
});

test("Resource form gives every field the same control height", async ({ page }) => {
  await page.goto("/");
  await page.getByTestId("open-resource").click({ force: true });
  await expect(page.getByRole("dialog")).toBeVisible();

  // The select trigger used to be h-10 next to h-9 inputs, so the two controls in the
  // Allocation Mode / Base Availability row did not line up.
  const heights = await page
    .getByRole("dialog")
    .evaluate((el) =>
      [...el.querySelectorAll('input:not([type=checkbox]), button[role="combobox"]')].map(
        (n) => Math.round(n.getBoundingClientRect().height),
      ),
    );
  expect(heights.length).toBeGreaterThan(2);
  expect([...new Set(heights)]).toHaveLength(1);
});

import { test, expect, type ConsoleMessage, type Page } from "@playwright/test";

/**
 * Cross-engine smoke checks for a slice of real foundation components rendered
 * against fixture data (see e2e/harness). Each check asserts something concrete —
 * the point is to catch parse errors from too-new syntax and gross render
 * differences across chromium/webkit/firefox, not to re-test component logic.
 */

// Collect console.error + uncaught page errors for the zero-error assertion.
function trackErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on("console", (msg: ConsoleMessage) => {
    if (msg.type() === "error") errors.push(msg.text());
  });
  page.on("pageerror", (err) => errors.push(err.message));
  return errors;
}

test("harness mounts with zero console errors", async ({ page }) => {
  const errors = trackErrors(page);
  await page.goto("/");
  // Wait for the heaviest fixture (FullCalendar) to paint before judging.
  await expect(page.getByText("Fixture Event")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Foundation smoke harness" })).toBeVisible();
  expect(errors, `console errors:\n${errors.join("\n")}`).toEqual([]);
});

test("FormDialog opens and Enter submits the form", async ({ page }) => {
  await page.goto("/");
  await page.getByTestId("open-form").click();
  const input = page.getByTestId("task-name");
  await expect(input).toBeVisible();
  await input.fill("Buy milk");
  await input.press("Enter");
  await expect(page.getByTestId("form-result")).toHaveText("Submitted: Buy milk");
});

test("OrkyoDataTable renders rows and pagination advances the page", async ({ page }) => {
  await page.goto("/");
  const table = page.getByTestId("data-table");
  await expect(table.getByText("Item 1")).toBeVisible();
  await expect(table.getByText(/Page 1 of 3/)).toBeVisible();

  await table.getByRole("button", { name: "Next page" }).click();

  await expect(table.getByText(/Page 2 of 3/)).toBeVisible();
  await expect(table.getByText("Item 3")).toBeVisible();
  await expect(table.getByText("Item 1")).toHaveCount(0);
});

test("utilization grid renders the off-time hatch cell", async ({ page }) => {
  await page.goto("/");
  const cell = page.getByTestId("offtime-cell");
  await expect(cell).toBeVisible();
  const backgroundImage = await cell.evaluate(
    (el) => getComputedStyle(el).backgroundImage,
  );
  expect(backgroundImage).toContain("repeating-linear-gradient");
});

test("RequestCalendar renders its fixture event", async ({ page }) => {
  await page.goto("/");
  await expect(
    page.getByTestId("calendar").getByText("Fixture Event"),
  ).toBeVisible();
});

test("RequestCalendar partitions overlapping events side by side", async ({ page }) => {
  // The two fixture events overlap 10:00-11:00. With slotEventOverlap off, the real
  // layout engine must give them disjoint horizontal ranges — the old default
  // stretched them across each other, burying whichever painted first.
  await page.goto("/");
  const calendar = page.getByTestId("calendar");
  await expect(calendar.getByText("Fixture Event")).toBeVisible();
  await expect(calendar.getByText("Overlapping Event")).toBeVisible();

  // Measure the positioned harness elements — the layout engine's own output —
  // not the inner text spans, whose rects can exceed their clipped container.
  const harnesses = calendar.locator(".fc-timegrid-event-harness");
  await expect(harnesses).toHaveCount(2);
  const a = await harnesses.nth(0).boundingBox();
  const b = await harnesses.nth(1).boundingBox();
  if (!a || !b) throw new Error("both events must have a bounding box");
  const disjoint = a.x + a.width <= b.x + 1 || b.x + b.width <= a.x + 1;
  expect(disjoint, "overlapping events must occupy disjoint columns").toBe(true);
});

test("ScheduleSlotDialog opens and lists the fixture backlog", async ({ page }) => {
  await page.goto("/");
  await page.getByTestId("open-schedule").click();
  await page.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: "Backlog Task" })).toBeVisible();
  await expect(
    page.getByRole("option", { name: "Spare Rigging Crew" }),
  ).toBeVisible();
});

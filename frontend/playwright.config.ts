import { defineConfig, devices } from "@playwright/test";

/**
 * Cross-engine smoke suite (measure M7). ON-DEMAND ONLY — deliberately NOT wired
 * into CI. It boots the fixture-only harness (e2e/harness) and renders a handful
 * of real foundation components in chromium/webkit/firefox to catch parse errors
 * from too-new syntax and gross cross-engine render breakage.
 *
 * Two lanes, both on-demand (never in PR CI — see e2e/README.md):
 *   - Desktop cross-engine (`chromium`/`firefox`/`webkit`): every *.spec.ts EXCEPT
 *     mobile.spec.ts. Run: `npm run test:browsers`.
 *   - Mobile/tablet viewport (`mobile-webkit` iPhone 14, `mobile-chromium` Pixel 7,
 *     `tablet-webkit` iPad gen 7): ONLY mobile.spec.ts, which asserts the phone
 *     card-mode / dvh dialog / scrollable wizard tabs / compressed calendar
 *     behaviours. Run: `npm run test:mobile`.
 *
 * If real browsers are ever wanted in automation, add a manual `workflow_dispatch`
 * GitHub Action that runs these — do NOT add them to the PR CI gate.
 *
 * Prerequisite: `npx playwright install --with-deps` (browsers + system libs).
 */
const PORT = 5188;

/** Restricts a project to the mobile viewport suite (mobile.spec.ts). */
const MOBILE_SPEC = /.*mobile\.spec\.ts/;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  reporter: "list",
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: `http://localhost:${PORT}`,
    trace: "on-first-retry",
  },
  projects: [
    // Desktop cross-engine lane — skips the mobile-viewport suite.
    { name: "chromium", use: { ...devices["Desktop Chrome"] }, testIgnore: MOBILE_SPEC },
    { name: "firefox", use: { ...devices["Desktop Firefox"] }, testIgnore: MOBILE_SPEC },
    { name: "webkit", use: { ...devices["Desktop Safari"] }, testIgnore: MOBILE_SPEC },
    // Mobile/tablet viewport lane — runs only mobile.spec.ts.
    { name: "mobile-webkit", use: { ...devices["iPhone 14"] }, testMatch: MOBILE_SPEC },
    { name: "mobile-chromium", use: { ...devices["Pixel 7"] }, testMatch: MOBILE_SPEC },
    { name: "tablet-webkit", use: { ...devices["iPad (gen 7)"] }, testMatch: MOBILE_SPEC },
  ],
  webServer: {
    command: `npx vite --config e2e/harness/vite.config.ts --port ${PORT} --strictPort`,
    url: `http://localhost:${PORT}`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});

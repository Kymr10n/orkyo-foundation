import { defineConfig, devices } from "@playwright/test";

/**
 * Cross-engine smoke suite (measure M7). ON-DEMAND ONLY — deliberately NOT wired
 * into CI. It boots the fixture-only harness (e2e/harness) and renders a handful
 * of real foundation components in chromium/webkit/firefox to catch parse errors
 * from too-new syntax and gross cross-engine render breakage.
 *
 * Prerequisite: `npx playwright install --with-deps` (browsers + system libs).
 * Run: `npm run test:browsers`. See e2e/README.md.
 */
const PORT = 5188;

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
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "firefox", use: { ...devices["Desktop Firefox"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
  ],
  webServer: {
    command: `npx vite --config e2e/harness/vite.config.ts --port ${PORT} --strictPort`,
    url: `http://localhost:${PORT}`,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});

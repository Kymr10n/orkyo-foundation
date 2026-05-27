import { defineConfig, devices } from '@playwright/test';

/**
 * E2E isolation tests for the embedded reports feature.
 *
 * These tests require a running full stack (backend + Superset + two provisioned
 * tenants).  They are skipped automatically when the required environment
 * variables are not set, so they never block unit-test runs or Vitest CI.
 *
 * Required environment variables:
 *   PLAYWRIGHT_BASE_URL       — frontend URL, e.g. http://localhost:5173
 *   TENANT_A_EMAIL            — login email for tenant A test user
 *   TENANT_A_PASSWORD         — password for tenant A test user
 *   TENANT_A_SLUG             — tenant A subdomain slug
 *   TENANT_B_EMAIL            — login email for tenant B test user
 *   TENANT_B_PASSWORD         — password for tenant B test user
 *   TENANT_B_SLUG             — tenant B subdomain slug
 */
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '**/*.spec.ts',
  timeout: 60_000,
  retries: 1,
  workers: 1,
  reporter: [['html', { open: 'never' }], ['list']],

  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});

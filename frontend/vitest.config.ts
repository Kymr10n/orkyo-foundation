import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { configDefaults, defineConfig } from "vitest/config";

// Logic tests that never touch the DOM run in the `node` project: no happy-dom window to
// build per file (the environment phase) and no Testing Library setup. This is an explicit
// allowlist, never "a directory minus exceptions": the dom project excludes exactly these
// patterns, so a file that matches here runs in node and nowhere else, and a file that
// matches nowhere here runs in dom. A test that needs window, document, storage, a
// @testing-library helper, a zustand persist store or config/runtime (it reads
// window.__RUNTIME_CONFIG__ at load) belongs in dom.
const NODE_TESTS = [
  "contracts/{plans,request-contract}.test.ts",
  "src/domain/**/*.test.ts",
  "src/constants/*.test.ts",
  "src/components/{assistant,capabilities,lists,resources,settings,ui,utilization}/*.test.ts",
  "src/lib/utils/{custom-field-value,duplicate-resource,export-handlers,picker-utils,spreadsheet-import,stable-stringify,time-navigation,utils,validation,variants}.test.ts",
  "src/lib/core/{api-paths,ids,invalidate-request-data,logger}.test.ts",
  "src/lib/{formatters,quotas/quota-display,table/filter-fns}.test.ts",
  "src/lib/api/{api-access-tokens-api,insights-api,lists-api,resource-assignments-api,resource-type-catalog-api}.test.ts",
  "src/store/{scheduler-store,ui-actions-store}.test.ts",
  "src/hooks/useTemplateForm.test.ts",
];

const ALL_TESTS = ["contracts/**/*.test.ts", "src/**/*.test.ts", "src/**/*.test.tsx"];

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@foundation/contracts": fileURLToPath(
        new URL("./contracts", import.meta.url),
      ),
      "@foundation/src": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    // Above setup.ts's asyncUtilTimeout (5000): vitest's 5000 default would kill a test
    // before Testing Library could report which query timed out, turning a clear failure
    // into "test timed out".
    testTimeout: 15000,
    globals: true,
    // Every mock's calls are cleared before each test, so no file restates vi.clearAllMocks().
    clearMocks: true,
    projects: [
      {
        extends: true,
        test: {
          name: "node",
          environment: "node",
          setupFiles: "./src/test/setup.node.ts",
          include: NODE_TESTS,
        },
      },
      {
        extends: true,
        test: {
          name: "dom",
          environment: "happy-dom",
          setupFiles: "./src/test/setup.ts",
          include: ALL_TESTS,
          exclude: [...configDefaults.exclude, ...NODE_TESTS],
        },
      },
    ],
    coverage: {
      // "cobertura" is required: release-ci.yml uploads coverage/cobertura-coverage.xml
      reporter: ["text", "lcov", "cobertura"],
      include: ["src/**/*.{ts,tsx}", "contracts/**/*.ts"],
      // Same bar as the saas/community frontends
      thresholds: { lines: 80, statements: 80, branches: 70, functions: 80 },
      exclude: [
        "src/test/**",
        // Test fixtures and helpers: codecov.yml ignores them too
        "src/test-utils/**",
        "**/*.test.{ts,tsx}",
        "**/*.d.ts",
        // Pure type definitions — nothing to assert at runtime
        "src/components/utilization/scheduler-types.ts",
        // Canvas / SVG rendering — no DOM surface to test without a real browser
        "src/components/requests/SpaceDrawingCanvas.tsx",
        "src/components/requests/DrawingPreviewSvg.tsx",
        "src/components/requests/SpaceShapeSvg.tsx",
      ],
    },
  },
});

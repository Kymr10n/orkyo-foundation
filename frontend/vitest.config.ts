import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
      "@foundation/contracts": fileURLToPath(
        new URL("./contracts", import.meta.url),
      ),
      "@foundation/src": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    environment: "happy-dom",
    globals: true,
    setupFiles: "./src/test/setup.ts",
    include: ["contracts/**/*.test.ts", "src/**/*.test.ts", "src/**/*.test.tsx"],
    coverage: {
      // "cobertura" is required: release-ci.yml uploads coverage/cobertura-coverage.xml
      reporter: ["text", "lcov", "cobertura"],
      include: ["src/**/*.{ts,tsx}", "contracts/**/*.ts"],
      // Same bar as the saas/community frontends
      thresholds: { lines: 80, statements: 80, branches: 70, functions: 80 },
      exclude: [
        "src/test/**",
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

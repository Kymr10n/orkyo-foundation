import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["contracts/**/*.test.ts"],
  },
});

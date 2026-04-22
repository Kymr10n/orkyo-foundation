import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";

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
  },
});

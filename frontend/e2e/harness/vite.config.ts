import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";

// Fixture-only dev server for the cross-engine Playwright smoke suite. Foundation
// is a library (no dev server of its own); this harness mounts a handful of real
// components against fixture data so they can be exercised in chromium/webkit/
// firefox. Not shipped — see e2e/README.md.
const foundationSrc = fileURLToPath(new URL("../../src", import.meta.url));
const foundationContracts = fileURLToPath(
  new URL("../../contracts", import.meta.url),
);
const permissionsStub = fileURLToPath(
  new URL("./permissions-stub.ts", import.meta.url),
);

export default defineConfig({
  root: fileURLToPath(new URL(".", import.meta.url)),
  plugins: [tailwindcss(), react()],
  resolve: {
    alias: [
      // Pin the permission gate to "can edit" without a backend/AuthContext.
      // Must precede the broader @foundation/src prefix alias below.
      { find: /^@foundation\/src\/hooks\/usePermissions$/, replacement: permissionsStub },
      { find: "@foundation/contracts", replacement: foundationContracts },
      { find: "@foundation/src", replacement: foundationSrc },
      { find: "@", replacement: foundationSrc },
    ],
  },
  server: {
    fs: {
      // Foundation source + node_modules live above the harness root.
      allow: [fileURLToPath(new URL("../..", import.meta.url))],
    },
  },
});

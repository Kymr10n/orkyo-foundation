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
const apiStubsDir = fileURLToPath(new URL("./api-stubs", import.meta.url));

export default defineConfig({
  root: fileURLToPath(new URL(".", import.meta.url)),
  plugins: [tailwindcss(), react()],
  resolve: {
    alias: [
      // Pin the permission gate to "can edit" without a backend/AuthContext.
      // Must precede the broader @foundation/src prefix alias below.
      { find: /^@foundation\/src\/hooks\/usePermissions$/, replacement: permissionsStub },
      // RequestFormDialog visual review: swap the backend-calling API modules
      // it (and its child sections) hit for fixed, no-network stubs backed by
      // e2e/harness/requests-fixtures.ts. Must precede the broader
      // @foundation/src prefix alias below.
      { find: /^@foundation\/src\/lib\/api\/request-api$/, replacement: `${apiStubsDir}/request-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/criteria-api$/, replacement: `${apiStubsDir}/criteria-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/template-api$/, replacement: `${apiStubsDir}/template-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/site-api$/, replacement: `${apiStubsDir}/site-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/space-api$/, replacement: `${apiStubsDir}/space-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/resources-api$/, replacement: `${apiStubsDir}/resources-api.ts` },
      { find: /^@foundation\/src\/lib\/api\/resource-assignments-api$/, replacement: `${apiStubsDir}/resource-assignments-api.ts` },
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

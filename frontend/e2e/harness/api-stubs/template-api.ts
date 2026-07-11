/*
 * Harness-only override for @foundation/src/lib/api/template-api.
 * RequestFormDialog only calls getTemplates('request') while open — not needed
 * for the dialog visual review, so it resolves an empty list.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { Template } from "@foundation/src/types/templates";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export {
  createTemplate,
  updateTemplate,
  deleteTemplate,
} from "../../../src/lib/api/template-api";

export async function getTemplates(): Promise<Template[]> {
  return [];
}

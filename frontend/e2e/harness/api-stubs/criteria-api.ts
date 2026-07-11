/*
 * Harness-only override for @foundation/src/lib/api/criteria-api.
 * RequestFormDialog only calls getCriteria() while open — no criteria fixtures
 * are needed for the dialog visual review, so it resolves an empty list.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { Criterion } from "@foundation/src/types/criterion";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export {
  createCriterion,
  updateCriterion,
  deleteCriterion,
  updateCriterionApplicability,
} from "../../../src/lib/api/criteria-api";

export async function getCriteria(): Promise<Criterion[]> {
  return [];
}

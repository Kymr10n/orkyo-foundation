import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

/**
 * Whether the current tenant is entitled to data export / import.
 *
 * Gates the TopBar Import/Export dialog and the organization-settings export card. The
 * server enforces `FeatureKeys.DataExport` on the organization JSON export; the per-page
 * CSV/JSON flows are client-side, so this hook IS their gate — a commercial nudge, not a
 * security boundary.
 */
export function useDataExportAvailable(): boolean {
  return useFeatureEnabled(FeatureKeys.DataExport);
}

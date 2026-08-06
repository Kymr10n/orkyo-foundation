import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

/**
 * Whether the current tenant is entitled to reporting API access.
 *
 * Presentation only — the server enforces `FeatureKeys.ApiAccess` on the token endpoints.
 */
export function useReportingApiAvailable(): boolean {
  return useFeatureEnabled(FeatureKeys.ApiAccess);
}

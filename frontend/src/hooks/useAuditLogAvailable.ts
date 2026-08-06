import { FeatureKeys } from "@foundation/contracts/plans";
import { useFeatureEnabled } from "@foundation/src/hooks/useFeatureEnabled";

/**
 * Whether the current tenant is entitled to the audit log.
 *
 * Presentation only — the server enforces `FeatureKeys.AuditLog` on the audit endpoints.
 */
export function useAuditLogAvailable(): boolean {
  return useFeatureEnabled(FeatureKeys.AuditLog);
}

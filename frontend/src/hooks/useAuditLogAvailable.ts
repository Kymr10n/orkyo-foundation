import { useAuth } from "@foundation/src/contexts/AuthContext";
import { SERVICE_TIER, isProfessionalOrAbove } from "@foundation/src/lib/api/admin-api";

/**
 * Whether the current tenant's tier includes the audit log.
 *
 * Site admins and break-glass sessions bypass the tier gate (operational work inside
 * a tenant; break-glass memberships carry no `tier` and would otherwise default to Free).
 * Mirrors `useReportingApiAvailable`.
 */
export function useAuditLogAvailable(): boolean {
  const { membership, isSiteAdmin } = useAuth();

  if (isSiteAdmin || membership?.isBreakGlass) return true;

  const tier = membership?.tier ?? SERVICE_TIER.FREE;
  return isProfessionalOrAbove(tier);
}

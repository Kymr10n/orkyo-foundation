import { useAuth } from "@foundation/src/contexts/AuthContext";
import { SERVICE_TIER, isProfessionalOrAbove } from "@foundation/src/lib/api/admin-api";

/**
 * Whether the current tenant's tier includes reporting API access.
 *
 * Site admins and break-glass sessions bypass the tier gate because they are
 * doing operational work inside a tenant. Break-glass memberships also carry no
 * `tier` field, which would otherwise default to "Free" and wrongly gate them.
 *
 * Mirrors `useAutoScheduleAvailable` (see hooks/useAutoSchedule.ts).
 */
export function useReportingApiAvailable(): boolean {
  const { membership, isSiteAdmin } = useAuth();

  // Site admins (including break-glass entry) are not subject to tier restrictions.
  if (isSiteAdmin || membership?.isBreakGlass) return true;

  const tier = membership?.tier ?? SERVICE_TIER.FREE;
  return isProfessionalOrAbove(tier);
}

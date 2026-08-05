import { useAuth } from "@foundation/src/contexts/AuthContext";
import { SERVICE_TIER, isProfessionalOrAbove } from "@foundation/src/lib/api/admin-api";

/**
 * Whether the current tenant's tier includes data export / import.
 *
 * Presentation only — this gates the TopBar Import/Export dialog and the
 * organization-settings export card. The server enforces `FeatureKeys.DataExport`
 * on the organization JSON export; the per-page CSV/JSON flows are client-side,
 * so this hook IS their gate — a commercial nudge, not a security boundary.
 *
 * Site admins and break-glass sessions bypass the tier gate because they are
 * doing operational work inside a tenant. Break-glass memberships also carry no
 * `tier` field, which would otherwise default to "Free" and wrongly gate them.
 *
 * Mirrors `useReportingApiAvailable` (see hooks/useReportingApiAvailable.ts).
 */
export function useDataExportAvailable(): boolean {
  const { membership, isSiteAdmin } = useAuth();

  // Site admins (including break-glass entry) are not subject to tier restrictions.
  if (isSiteAdmin || membership?.isBreakGlass) return true;

  const tier = membership?.tier ?? SERVICE_TIER.FREE;
  return isProfessionalOrAbove(tier);
}

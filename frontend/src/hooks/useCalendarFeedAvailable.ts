import { useAuth } from "@foundation/src/contexts/AuthContext";
import { SERVICE_TIER, isProfessionalOrAbove } from "@foundation/src/lib/api/admin-api";

/**
 * Whether the current tenant's tier includes calendar subscriptions (.ics feeds).
 *
 * Presentation only — the server enforces `FeatureKeys.CalendarFeed` on both the
 * create endpoint (402) and the feed itself (404). This just decides whether the
 * dialog shows the feature or the upsell.
 *
 * Site admins and break-glass sessions bypass the tier gate because they are
 * doing operational work inside a tenant. Break-glass memberships also carry no
 * `tier` field, which would otherwise default to "Free" and wrongly gate them.
 *
 * Mirrors `useReportingApiAvailable` (see hooks/useReportingApiAvailable.ts).
 */
export function useCalendarFeedAvailable(): boolean {
  const { membership, isSiteAdmin } = useAuth();

  // Site admins (including break-glass entry) are not subject to tier restrictions.
  if (isSiteAdmin || membership?.isBreakGlass) return true;

  const tier = membership?.tier ?? SERVICE_TIER.FREE;
  return isProfessionalOrAbove(tier);
}

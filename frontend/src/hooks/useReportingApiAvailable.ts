import { useAuth } from "@foundation/src/contexts/AuthContext";
import { PLAN_FEATURES } from "@foundation/src/lib/generated/plan-data";

type TierKey = "free" | "professional" | "enterprise";

/**
 * Whether the current tenant's tier includes reporting API access.
 *
 * Driven by the "API access" row of the plan matrix
 * (`src/lib/generated/plan-data.ts`) → Professional + Enterprise, not Free.
 * Community resolves tenants as Enterprise, so it is always available there.
 *
 * Mirrors `useAutoScheduleAvailable` (see hooks/useAutoSchedule.ts).
 */
export function useReportingApiAvailable(): boolean {
  const { membership } = useAuth();
  const tier = membership?.tier ?? "Free";
  const key = tier.toLowerCase() as TierKey;
  const apiAccess = PLAN_FEATURES.find((f) => f.label === "API access");
  return apiAccess?.[key] === true;
}

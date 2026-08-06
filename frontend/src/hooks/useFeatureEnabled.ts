import { useAuth } from "@foundation/src/contexts/AuthContext";
import type { FeatureKey } from "@foundation/contracts/plans";

/**
 * Whether a server-enforced feature is available to the current tenant.
 *
 * Reads the entitlements the server computed and ships with the session — the same
 * plan → feature mapping it enforces with (`IFeatureGate`, 402/404 on the gated
 * endpoints). Clients must not re-derive this from the plan code: that duplicates the
 * entitlement table, ignores per-tenant overrides, and is how a single casing slip once
 * locked every gated feature for paying tenants.
 *
 * Presentation only — this decides whether to show the feature or its upsell, never
 * whether access is allowed. A missing entitlement (older backend, unresolved tenant)
 * fails closed, matching the server's default-deny.
 *
 * Site admins and break-glass sessions bypass the gate because they are doing
 * operational work inside a tenant, and break-glass memberships carry no entitlements.
 */
export function useFeatureEnabled(feature: FeatureKey): boolean {
  const { membership, isSiteAdmin } = useAuth();

  if (isSiteAdmin || membership?.isBreakGlass) return true;

  return membership?.entitlements?.[feature] === true;
}

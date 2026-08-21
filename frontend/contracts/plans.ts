/**
 * Plan codes and feature keys - mirrors the backend wire contract.
 *
 * Plan codes MUST stay in sync with orkyo-saas backend/src/Models/TierCodes.cs and
 * SinglePlanInfoProvider.PlanCode; feature keys with backend FeatureKeys (IFeatureGate.cs).
 * Any changes to these values must be coordinated across FE/BE.
 *
 * The wire carries the machine CODE ("enterprise"), never the display label
 * ("Enterprise") — comparing a label against these codes silently reads as "not
 * entitled", which once locked five features for every paying tenant.
 */

export const PlanCodes = {
  /** Self-hosted Community — a single unlimited plan, not purchasable. */
  Community: "community",
  Free: "free",
  Professional: "professional",
  Enterprise: "enterprise",
} as const;

export type PlanCode = (typeof PlanCodes)[keyof typeof PlanCodes];

/** Feature keys the server enforces and reports in the session payload. */
export const FeatureKeys = {
  ApiAccess: "api_access_enabled",
  AuditLog: "audit_log_enabled",
  DataExport: "data_export_enabled",
  CalendarFeed: "calendar_feed_enabled",
  AiAssistant: "ai_assistant_enabled",
} as const;

export type FeatureKey = (typeof FeatureKeys)[keyof typeof FeatureKeys];

/** Plans whose entitlements include the premium feature set. */
const PREMIUM_PLAN_CODES: readonly PlanCode[] = [
  PlanCodes.Community,
  PlanCodes.Professional,
  PlanCodes.Enterprise,
];

/**
 * Whether this plan includes the premium feature set.
 *
 * Only for the few product decisions the server does NOT compute an entitlement for
 * (auto-schedule availability, the Sites tab). Anything the server enforces must be
 * gated with `useFeatureEnabled` instead — re-deriving those from the plan duplicates
 * the server's plan → feature table and ignores per-tenant overrides.
 *
 * Allow-list, so an unknown or renamed code fails CLOSED, matching the server's
 * default-deny gate. Presentation only — never an enforcement point.
 */
export function planIncludesPremiumFeatures(plan: PlanCode): boolean {
  return PREMIUM_PLAN_CODES.includes(plan);
}

/** Boundary guard: is this raw wire value a plan code we recognise? */
export function isKnownPlanCode(value: string): value is PlanCode {
  return (Object.values(PlanCodes) as readonly string[]).includes(value);
}

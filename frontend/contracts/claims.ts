/**
 * JWT and identity claim type constants - mirrors backend ClaimConstants.cs
 *
 * These MUST stay in sync with backend/api/Constants/ClaimConstants.cs
 * Any changes to these values must be coordinated across FE/BE.
 */

export const Claims = {
  /** Internal user ID claim */
  UserId: "user_id",

  /** Tenant slug claim */
  TenantSlug: "tenant_slug",

  /** Tenant ID claim */
  TenantId: "tenant_id",

  /** Tenant admin flag claim */
  IsTenantAdmin: "is_tenant_admin",

  /** Keycloak subject claim */
  Subject: "sub",

  /** Email claim (Keycloak) */
  Email: "email",

  /** Preferred username claim (Keycloak) */
  PreferredUsername: "preferred_username",
} as const;


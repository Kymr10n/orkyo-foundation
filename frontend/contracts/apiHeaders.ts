/**
 * API header constants - mirrors backend/src/Constants/HeaderConstants.cs
 *
 * These MUST stay in sync with backend constants.
 */

export const ApiHeaders = {
  TenantSlug: "X-Tenant-Slug",
  CorrelationId: "X-Correlation-ID",
} as const;

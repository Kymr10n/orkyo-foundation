/**
 * Admin API client for site administration control plane
 *
 * These endpoints require the site-admin role in Keycloak.
 * Used by the AdminPage for managing tenants, users, and memberships.
 */

import { apiGet, apiPost, apiPatch, apiDelete, apiPut } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

// ============================================================================
// Types
// ============================================================================

export type ServiceTier = 'free' | 'professional' | 'enterprise';

/** Mirrors backend TenantStatusConstants and the DB check constraint. */
export type TenantStatus = 'active' | 'suspended' | 'deleting';

export const TENANT_STATUS = {
  ACTIVE: 'active',
  SUSPENDED: 'suspended',
  DELETING: 'deleting',
} as const satisfies Record<string, TenantStatus>;

export const SERVICE_TIER = {
  FREE: 'free',
  PROFESSIONAL: 'professional',
  ENTERPRISE: 'enterprise',
} as const satisfies Record<string, ServiceTier>;

export const TIER_DISPLAY_NAMES: Record<ServiceTier, string> = {
  free: 'Free',
  professional: 'Professional',
  enterprise: 'Enterprise',
};

/**
 * Single source of truth for the "Professional tier or above" gate.
 * Consumers (reporting API, auto-schedule, …) must use this so a tier rename
 * is a compile error everywhere rather than a silently-stale string literal.
 */
export function isProfessionalOrAbove(tier: ServiceTier): boolean {
  return tier === SERVICE_TIER.PROFESSIONAL || tier === SERVICE_TIER.ENTERPRISE;
}

export interface AdminTenant {
  id: string;
  slug: string;
  displayName: string;
  status: TenantStatus;
  dbIdentifier: string;
  createdAt: string;
  updatedAt: string;
  memberCount?: number;
  tier: ServiceTier;
}

export interface AdminUser {
  id: string;
  email: string;
  displayName: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  lastLoginAt: string | null;
  membershipCount?: number;
  identityCount?: number;
  isSiteAdmin: boolean;
  ownedTenantId: string | null;
  ownedTenantTier: ServiceTier | null;
}

export interface AdminUserDetail extends AdminUser {
  identities: AdminUserIdentity[];
  memberships: AdminUserMembership[];
}

export interface AdminUserIdentity {
  id: string;
  provider: string;
  providerSubject: string;
  providerEmail: string | null;
  createdAt: string;
}

export interface AdminUserMembership {
  tenantId: string;
  tenantSlug: string;
  tenantName: string;
  role: string;
  status: string;
  joinedAt: string;
}

export interface AdminTenantMember {
  userId: string;
  email: string;
  displayName: string | null;
  userStatus: string;
  role: string;
  membershipStatus: string;
  joinedAt: string;
}

// ============================================================================
// Tenant Management
// ============================================================================

export async function getAdminTenants(search?: string): Promise<{ tenants: AdminTenant[] }> {
  const params: Record<string, string> = {};
  if (search) params.search = search;
  return apiGet<{ tenants: AdminTenant[] }>(API_PATHS.ADMIN.TENANTS, { params });
}

export async function createAdminTenant(data: { slug: string; displayName: string }): Promise<AdminTenant> {
  return apiPost<AdminTenant>(API_PATHS.ADMIN.TENANTS, data);
}

export async function updateAdminTenant(
  tenantId: string,
  data: { displayName?: string; status?: string }
): Promise<{ message: string }> {
  return apiPatch<{ message: string }>(API_PATHS.ADMIN.tenant(tenantId), data);
}

export async function updateAdminTenantTier(
  tenantId: string,
  tier: ServiceTier
): Promise<{ message: string; tier: string }> {
  return apiPatch<{ message: string; tier: string }>(API_PATHS.ADMIN.tenantTier(tenantId), { tier });
}

export async function deleteAdminTenant(tenantId: string): Promise<void> {
  await apiDelete(API_PATHS.ADMIN.tenant(tenantId));
}

// ============================================================================
// User Management
// ============================================================================

export async function getAdminUsers(
  search?: string,
  status?: string
): Promise<{ users: AdminUser[] }> {
  const params: Record<string, string> = {};
  if (search) params.search = search;
  if (status) params.status = status;
  return apiGet<{ users: AdminUser[] }>(API_PATHS.ADMIN.USERS, { params });
}

export async function getAdminUser(userId: string): Promise<AdminUserDetail> {
  return apiGet<AdminUserDetail>(API_PATHS.ADMIN.user(userId));
}

export async function deactivateAdminUser(userId: string): Promise<void> {
  await apiPost<void>(API_PATHS.ADMIN.userDeactivate(userId), {});
}

export async function reactivateAdminUser(userId: string): Promise<void> {
  await apiPost<void>(API_PATHS.ADMIN.userReactivate(userId), {});
}

export async function deleteAdminUser(userId: string): Promise<void> {
  await apiDelete(API_PATHS.ADMIN.user(userId));
}

export async function promoteSiteAdmin(userId: string): Promise<void> {
  await apiPost<void>(API_PATHS.ADMIN.userPromoteSiteAdmin(userId), {});
}

export async function revokeSiteAdmin(userId: string): Promise<void> {
  await apiPost<void>(API_PATHS.ADMIN.userRevokeSiteAdmin(userId), {});
}

// ============================================================================
// Tenant Membership Management
// ============================================================================

export async function getAdminTenantMembers(
  tenantId: string,
  status?: string
): Promise<{ tenantId: string; tenantSlug: string; members: AdminTenantMember[] }> {
  const params: Record<string, string> = {};
  if (status) params.status = status;
  return apiGet<{ tenantId: string; tenantSlug: string; members: AdminTenantMember[] }>(
    API_PATHS.ADMIN.tenantMembers(tenantId),
    { params }
  );
}

export async function addAdminTenantMember(
  tenantId: string,
  data: { userId: string; role: string }
): Promise<{ userId: string; tenantId: string; role: string; status: string }> {
  return apiPost<{ userId: string; tenantId: string; role: string; status: string }>(
    API_PATHS.ADMIN.tenantMembers(tenantId),
    data
  );
}

export async function updateAdminTenantMember(
  tenantId: string,
  userId: string,
  data: { role?: string; status?: string }
): Promise<{ userId: string; tenantId: string; role: string; status: string }> {
  return apiPatch<{ userId: string; tenantId: string; role: string; status: string }>(
    API_PATHS.ADMIN.tenantMember(tenantId, userId),
    data
  );
}

export async function removeAdminTenantMember(
  tenantId: string,
  userId: string
): Promise<void> {
  await apiDelete(API_PATHS.ADMIN.tenantMember(tenantId, userId));
}

export interface BreakGlassSessionStatus {
  sessionId: string;
  /** Slug of the tenant the session targets. Only present on the GET status response. */
  tenantSlug?: string;
  reason?: string;
  createdAt: string;
  expiresAt: string;
  /** Hard cap: createdAt + BreakGlassSessionAbsoluteCap. Renewals can never extend past this. */
  absoluteExpiresAt: string;
}

/**
 * Audit break-glass entry when site-admin accesses a tenant.
 * Returns the full session metadata (including the hard cap) so the caller can
 * drive a countdown banner on the tenant page.
 */
export async function auditBreakGlassEntry(
  tenantSlug: string,
  reason?: string,
): Promise<BreakGlassSessionStatus> {
  return apiPost<BreakGlassSessionStatus>(
    API_PATHS.ADMIN.BREAK_GLASS_ENTRY,
    { tenantSlug, reason },
  );
}

/**
 * Audit break-glass exit when site-admin leaves a tenant
 */
export async function auditBreakGlassExit(sessionId: string): Promise<void> {
  await apiPost<{ success: boolean }>(
    API_PATHS.ADMIN.BREAK_GLASS_EXIT,
    { sessionId }
  );
}

/**
 * Extend an active break-glass session. Returns the renewed session metadata.
 * If the session has reached the absolute hard cap the backend responds 410 Gone
 * with `code: break_glass_hard_cap_reached` and `handleApiError` will route the
 * admin back to /admin — so callers don't need their own hard-cap handling.
 */
export async function renewBreakGlassSession(sessionId: string): Promise<BreakGlassSessionStatus> {
  return apiPost<BreakGlassSessionStatus>(API_PATHS.ADMIN.BREAK_GLASS_RENEW, { sessionId });
}

/**
 * Read the current break-glass session for a tenant. Used to drive the countdown
 * banner and to detect external revocation. Returns null when there is no active
 * session for this admin / tenant pair (404 with `break_glass_expired`).
 */
export async function getBreakGlassSessionStatus(
  tenantSlug: string,
): Promise<BreakGlassSessionStatus | null> {
  try {
    return await apiGet<BreakGlassSessionStatus>(API_PATHS.ADMIN.breakGlassSession(tenantSlug));
  } catch {
    // handleApiError already handled the redirect case for `break_glass_expired`.
    // For other errors (network, etc.) fall back to "no session known" so the UI
    // can render without the banner instead of crashing.
    return null;
  }
}

// ============================================================================
// Platform Settings (admin settings endpoint)
// ============================================================================

export interface AdminSettingsResponse {
  runtime: {
    defaultTimezone: string;
    workingHoursStart: string;
    workingHoursEnd: string;
    holidayProviderEnabled: boolean;
    brandingName: string;
    brandingLogoUrl: string;
  };
  deployment: {
    publicUrl: string;
    authPublicUrl: string;
    smtpHost: string;
    smtpPort: number;
    keycloakRealm: string;
    logLevel: string;
  };
  systemInfo: {
    version: string;
    databaseStatus: string;
    smtpConfigured: boolean;
    authProvider: string;
    authRealm: string;
  };
}

export async function getAdminSettings(): Promise<AdminSettingsResponse> {
  return apiGet<AdminSettingsResponse>(API_PATHS.ADMIN.SETTINGS);
}

export async function updateAdminSettings(
  settings: Record<string, string>,
): Promise<{ runtime: AdminSettingsResponse['runtime']; updatedKeys: string[] }> {
  return apiPut<{ runtime: AdminSettingsResponse['runtime']; updatedKeys: string[] }>(
    API_PATHS.ADMIN.SETTINGS,
    { settings },
  );
}

// ============================================================================
// Diagnostics
// ============================================================================

export interface DiagnosticsResponse {
  version: string;
  build: string;
  deploymentMode: string;
  logLevel: string;
  database: {
    status: string;
    migrationsApplied: number;
    tenantCount: number;
  };
  smtp: {
    status: string;
    host: string;
  };
  auth: {
    status: string;
    provider: string;
    realm: string;
  };
  worker: {
    status: string;
    lastActivity: string | null;
  };
  modules: {
    observability: boolean;
    logAggregation: boolean;
  };
}

export async function getAdminDiagnostics(): Promise<DiagnosticsResponse> {
  return apiGet<DiagnosticsResponse>(API_PATHS.ADMIN.DIAGNOSTICS);
}

// ── Quota management ─────────────────────────────────────────────────────────

export interface TenantUsageRow {
  id: string;
  slug: string;
  displayName: string;
  tier: string;
  usage: {
    activeSeats: number;
    productionSites: number;
    spaces: number;
    storageBytes: number;
  };
}

export interface SubscriptionTierQuota {
  quotaKey: string;
  unit: string;
  limitValue: number | null;
  booleanValue: boolean | null;
  enforcementMode: string;
}

export interface SubscriptionTier {
  id: string;
  code: string;
  displayName: string;
  isPublic: boolean;
  sortOrder: number;
  quotas: SubscriptionTierQuota[];
}

export interface AdminTenantQuotaDetail {
  tenantId: string;
  tier: { code: string; displayName: string };
  numericLimits: Record<string, number>;
  featureEntitlements: Record<string, boolean>;
  activeOverrides: {
    quotaKey: string;
    unit: string;
    limitValue: number | null;
    booleanValue: boolean | null;
    reason: string | null;
    expiresAt: string | null;
  }[];
  liveUsage?: {
    active_seats: number;
    production_sites: number;
    spaces: number;
    storage_bytes: number;
  };
}

export async function updateAdminSubscriptionTierQuota(
  tierId: string,
  quotaKey: string,
  data: { limitValue?: number; booleanValue?: boolean; enforcementMode?: string },
): Promise<{ message: string }> {
  return apiPut<{ message: string }>(
    API_PATHS.ADMIN.subscriptionTierQuota(tierId, quotaKey),
    data,
  );
}

export async function getAdminTenantsUsage(): Promise<{ tenants: TenantUsageRow[] }> {
  return apiGet<{ tenants: TenantUsageRow[] }>(API_PATHS.ADMIN.TENANTS_USAGE);
}

export async function getAdminSubscriptionTiers(): Promise<{ tiers: SubscriptionTier[] }> {
  return apiGet<{ tiers: SubscriptionTier[] }>(API_PATHS.ADMIN.SUBSCRIPTION_TIERS);
}

export async function getAdminTenantQuotas(tenantId: string): Promise<AdminTenantQuotaDetail> {
  return apiGet<AdminTenantQuotaDetail>(API_PATHS.ADMIN.tenantQuotas(tenantId));
}

export async function upsertAdminQuotaOverride(
  tenantId: string,
  quotaKey: string,
  data: { limitValue?: number; booleanValue?: boolean; reason?: string; expiresAt?: string },
): Promise<{ message: string }> {
  return apiPut<{ message: string }>(
    API_PATHS.ADMIN.tenantQuotaOverride(tenantId, quotaKey),
    data,
  );
}

export async function deleteAdminQuotaOverride(
  tenantId: string,
  quotaKey: string,
): Promise<void> {
  await apiDelete(API_PATHS.ADMIN.tenantQuotaOverride(tenantId, quotaKey));
}

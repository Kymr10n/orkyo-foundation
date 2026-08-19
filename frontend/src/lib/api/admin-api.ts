/**
 * Admin API client for site administration control plane
 *
 * These endpoints require the site-admin role in Keycloak.
 * Used by the AdminPage for managing tenants, users, and memberships.
 */

import { apiGet, apiPost, apiPatch, apiDelete, apiPut } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { PlanCodes, planIncludesPremiumFeatures, type PlanCode } from '@foundation/contracts/plans';

// ============================================================================
// Types
// ============================================================================

/**
 * The SaaS tiers a platform admin can assign. Deliberately excludes `community`, which is
 * not purchasable and must never appear in TierSelect — see TIER_DISPLAY_NAMES below.
 * For the full wire vocabulary (including Community) use `PlanCode` from contracts/plans.
 */
export type ServiceTier = Exclude<PlanCode, 'community'>;

/** Mirrors backend TenantStatusConstants and the DB check constraint. */
export type TenantStatus = 'active' | 'suspended' | 'deleting';

export const TENANT_STATUS = {
  ACTIVE: 'active',
  SUSPENDED: 'suspended',
  DELETING: 'deleting',
} as const satisfies Record<string, TenantStatus>;

export const SERVICE_TIER = {
  FREE: PlanCodes.Free,
  PROFESSIONAL: PlanCodes.Professional,
  ENTERPRISE: PlanCodes.Enterprise,
} as const satisfies Record<string, ServiceTier>;

/**
 * Display names for the billable tiers. TierSelect iterates this to build its options, so
 * every key here becomes a selectable tier — never add `community`.
 */
export const TIER_DISPLAY_NAMES: Record<ServiceTier, string> = {
  free: 'Free',
  professional: 'Professional',
  enterprise: 'Enterprise',
};

/**
 * @deprecated Renamed: Community also passes this gate, so the old name was a lie.
 * Use `planIncludesPremiumFeatures` from `@foundation/contracts/plans` — and for anything
 * the server enforces, use `useFeatureEnabled` instead of any plan-derived check.
 * Removed at the next major.
 */
export const isProfessionalOrAbove = planIncludesPremiumFeatures;

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

/** Server-side paging request for the admin lists. 1-based page; the server clamps pageSize. */
export interface AdminListPaging {
  page: number;
  pageSize: number;
}

/**
 * Without `paging` the server answers with the full unpaged list and no paging fields —
 * the tenant-picker dropdown depends on that. With `paging` the response carries
 * page/pageSize/totalCount for a server-paged table.
 */
export async function getAdminTenants(
  search?: string,
  paging?: AdminListPaging
): Promise<{ tenants: AdminTenant[]; page?: number; pageSize?: number; totalCount?: number }> {
  const params: Record<string, string> = {};
  if (search) params.search = search;
  if (paging) {
    params.page = String(paging.page);
    params.pageSize = String(paging.pageSize);
  }
  return apiGet<{ tenants: AdminTenant[]; page?: number; pageSize?: number; totalCount?: number }>(
    API_PATHS.ADMIN.TENANTS,
    { params }
  );
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

/** Same opt-in paging contract as {@link getAdminTenants}: no `paging`, no paging fields. */
export async function getAdminTenantMembers(
  tenantId: string,
  status?: string,
  paging?: AdminListPaging
): Promise<{
  tenantId: string;
  tenantSlug: string;
  members: AdminTenantMember[];
  page?: number;
  pageSize?: number;
  totalCount?: number;
}> {
  const params: Record<string, string> = {};
  if (status) params.status = status;
  if (paging) {
    params.page = String(paging.page);
    params.pageSize = String(paging.pageSize);
  }
  return apiGet<{
    tenantId: string;
    tenantSlug: string;
    members: AdminTenantMember[];
    page?: number;
    pageSize?: number;
    totalCount?: number;
  }>(API_PATHS.ADMIN.tenantMembers(tenantId), { params });
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

// ============================================================================
// Platform Audit (control plane)
// ============================================================================

/**
 * A control-plane `audit_events` row: platform-wide, across all tenants, and
 * carrying the sensitive fields (`ipAddress`, `requestId`) that the tenant-facing
 * `/api/audit` deliberately omits.
 *
 * Distinct from `TenantAuditEvent` in `audit-api.ts` by design — that one reads the
 * tenant's OWN database and therefore requires a resolved tenant, which the
 * site-admin apex host never has.
 */
export interface PlatformAuditEvent {
  id: string;
  actorUserId: string | null;
  actorType: string; // 'user' | 'system' | 'api'
  action: string;
  targetType: string | null;
  targetId: string | null;
  metadata: string | null; // JSON string
  requestId: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export interface PlatformAuditPage {
  events: PlatformAuditEvent[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PlatformAuditFilters {
  action?: string;
  actorId?: string;
  targetType?: string;
  targetId?: string;
  from?: string;
  to?: string;
  page?: number; // 1-based (backend)
  pageSize?: number;
}

export async function getPlatformAuditEvents(
  filters?: PlatformAuditFilters,
): Promise<PlatformAuditPage> {
  const params: Record<string, string | number> = {};
  if (filters?.action) params.action = filters.action;
  if (filters?.actorId) params.actorId = filters.actorId;
  if (filters?.targetType) params.targetType = filters.targetType;
  if (filters?.targetId) params.targetId = filters.targetId;
  if (filters?.from) params.from = filters.from;
  if (filters?.to) params.to = filters.to;
  if (filters?.page !== undefined) params.page = filters.page;
  if (filters?.pageSize !== undefined) params.pageSize = filters.pageSize;
  return apiGet<PlatformAuditPage>(API_PATHS.ADMIN.AUDIT, { params });
}

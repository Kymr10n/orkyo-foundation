/**
 * API client for Tenant Settings operations
 *
 * Manages admin-configurable settings per tenant:
 * security, branding, uploads, search, invitations.
 */

import { apiGet, apiPut, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { ApiHeaders } from "@foundation/contracts/apiHeaders";
import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";
import type { ApiRequestOptions } from "@foundation/src/lib/core/api-client";

// ── Types ───────────────────────────────────────────────────────────

export type SettingScope = "site" | "tenant";

export interface TenantSettingDescriptor {
  key: string;
  category: string;
  displayName: string;
  description: string;
  valueType: "int" | "double" | "string" | "bool";
  defaultValue: string;
  /** "site" = site-admin only; "tenant" = tenant-admin and above */
  scope: SettingScope;
  minValue: string | null;
  maxValue: string | null;
  currentValue: string;
}

export interface TenantSettingsResponse {
  settings: TenantSettingDescriptor[];
}

// ── Helpers ─────────────────────────────────────────────────────────

/**
 * Resolve which settings surface a request targets, by slug:
 *  - `null`      → SITE scope: the site-admin control-plane endpoint (`/api/admin/configuration`,
 *                  RequireSiteAdmin). The tenant header is stripped so no tenant is resolved and the
 *                  backend serves site-scoped overrides (TenantSettingsService.IsSiteContext).
 *  - `string`    → a specific tenant (via the tenant header) on the tenant endpoint.
 *  - `undefined` → the current auth tenant on the tenant endpoint.
 */
function settingsTarget(slug?: string | null): {
  list: string;
  item: (key: string) => string;
  options?: ApiRequestOptions;
} {
  if (slug === null) {
    return {
      list: API_PATHS.ADMIN_CONFIGURATION,
      item: API_PATHS.adminConfigurationSetting,
      options: { omitHeaders: [TENANT_HEADER_NAME] },
    };
  }
  return {
    list: API_PATHS.SETTINGS,
    item: API_PATHS.setting,
    options: slug ? { headers: { [ApiHeaders.TenantSlug]: slug } } : undefined,
  };
}

// ── API calls ───────────────────────────────────────────────────────

/** Get all settings descriptors with current values.
 *  - Omit `tenantSlug` to use current auth tenant context.
 *  - Pass a string to target a specific tenant.
 *  - Pass `null` for site-level settings (site-admin control plane). */
export async function getTenantSettings(
  tenantSlug?: string | null,
): Promise<TenantSettingsResponse> {
  const target = settingsTarget(tenantSlug);
  return apiGet<TenantSettingsResponse>(target.list, target.options);
}

/** Update one or more settings.
 *  Pass `null` for site-level, a string for a specific tenant, or omit for current tenant. */
export async function updateTenantSettings(
  settings: Record<string, string>,
  tenantSlug?: string | null,
): Promise<TenantSettingsResponse> {
  const target = settingsTarget(tenantSlug);
  return apiPut<TenantSettingsResponse>(target.list, { settings }, target.options);
}

/** Reset a single setting to its compiled default.
 *  Pass `null` for site-level, a string for a specific tenant, or omit for current tenant. */
export async function resetTenantSetting(
  key: string,
  tenantSlug?: string | null,
): Promise<void> {
  const target = settingsTarget(tenantSlug);
  return apiDelete(target.item(key), target.options);
}

/**
 * API client for Tenant Settings operations
 *
 * Manages admin-configurable settings per tenant:
 * security, branding, uploads, search, invitations.
 */

import { apiGet, apiPut, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { ApiHeaders } from "@foundation/contracts/apiHeaders";
import { TENANT_HEADER_NAME } from "@/constants/http";
import type { ApiRequestOptions } from "@/lib/core/api-client";

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

/** Build header options for settings requests.
 *  - `undefined` → use default tenant from auth context
 *  - `string`    → override to that specific tenant
 *  - `null`      → site context: strip tenant header entirely */
function tenantHeaders(slug?: string | null): ApiRequestOptions | undefined {
  if (slug === null) {
    return { omitHeaders: [TENANT_HEADER_NAME] };
  }
  return slug ? { headers: { [ApiHeaders.TenantSlug]: slug } } : undefined;
}

// ── API calls ───────────────────────────────────────────────────────

/** Get all settings descriptors with current values.
 *  - Omit `tenantSlug` to use current auth tenant context.
 *  - Pass a string to target a specific tenant.
 *  - Pass `null` for site-level settings (no tenant). */
export async function getTenantSettings(
  tenantSlug?: string | null,
): Promise<TenantSettingsResponse> {
  return apiGet<TenantSettingsResponse>(API_PATHS.SETTINGS, tenantHeaders(tenantSlug));
}

/** Update one or more settings.
 *  Pass `null` for site-level, a string for a specific tenant, or omit for current tenant. */
export async function updateTenantSettings(
  settings: Record<string, string>,
  tenantSlug?: string | null,
): Promise<TenantSettingsResponse> {
  return apiPut<TenantSettingsResponse>(
    API_PATHS.SETTINGS,
    { settings },
    tenantHeaders(tenantSlug),
  );
}

/** Reset a single setting to its compiled default.
 *  Pass `null` for site-level, a string for a specific tenant, or omit for current tenant. */
export async function resetTenantSetting(
  key: string,
  tenantSlug?: string | null,
): Promise<void> {
  return apiDelete(API_PATHS.setting(key), tenantHeaders(tenantSlug));
}

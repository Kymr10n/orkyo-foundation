/**
 * Tenant Management API
 *
 * Within-tenant operations for owners: update settings, transfer ownership.
 * Calls are made with an active tenant context header.
 * For pre-auth / cross-tenant operations see tenant-account-api.ts.
 */

import { apiPost, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

interface TenantInfo {
  id: string;
  slug: string;
  displayName: string;
  status: "active" | "suspended" | "deleting";
}

interface UpdateTenantRequest {
  displayName?: string;
}

/**
 * Update tenant settings (owner only)
 */
export async function updateTenant(
  tenantId: string,
  data: UpdateTenantRequest
): Promise<TenantInfo> {
  return apiPatch<TenantInfo>(
    API_PATHS.TENANTS.byId(tenantId),
    data,
  );
}

/**
 * Transfer ownership to another admin (owner only)
 */
export async function transferTenantOwnership(
  tenantId: string,
  newOwnerId: string
): Promise<{ transferred: boolean }> {
  return apiPost<{ transferred: boolean }>(
    API_PATHS.TENANTS.transferOwnership(tenantId),
    { newOwnerId }
  );
}

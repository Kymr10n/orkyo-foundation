/**
 * API client for Tenant Management operations
 *
 * These endpoints are called without tenant context (before tenant selection)
 * and require only OIDC authentication.
 */

import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";
import { apiGet, apiPost, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

/** One row of the account page's organization list; not the auth session's `TenantMembership`. */
export interface AccountMembership {
  tenantId: string;
  tenantSlug: string;
  tenantDisplayName: string;
  tenantStatus: string;
  role: string;
  status: string;
  isOwner: boolean;
  joinedAt: string;
}

interface CanCreateTenantResponse {
  canCreate: boolean;
  reason?: string;
  currentCount?: number;
  maxAllowed?: number;
}

export interface CreateTenantRequest {
  slug: string;
  displayName: string;
  starterTemplate?: string;
}

export interface CreateTenantResponse {
  id: string;
  slug: string;
  displayName: string;
  state: string;
}

interface StarterTemplateInfo {
  key: string;
  name: string;
  description: string;
  icon: string;
  includesDemoData: boolean;
}

const tenantOptions = { omitHeaders: [TENANT_HEADER_NAME] };

export async function canCreateTenant(): Promise<CanCreateTenantResponse> {
  return apiGet<CanCreateTenantResponse>(API_PATHS.TENANTS.CAN_CREATE, tenantOptions);
}

export async function createTenant(request: CreateTenantRequest): Promise<CreateTenantResponse> {
  return apiPost<CreateTenantResponse>(
    API_PATHS.TENANTS.CREATE,
    request,
    tenantOptions,
  );
}

export async function getStarterTemplates(): Promise<StarterTemplateInfo[]> {
  return apiGet<StarterTemplateInfo[]>(API_PATHS.TENANTS.STARTER_TEMPLATES, tenantOptions);
}

export async function getTenantMemberships(): Promise<AccountMembership[]> {
  return apiGet<AccountMembership[]>(API_PATHS.TENANTS.MEMBERSHIPS, tenantOptions);
}

/**
 * Leave a tenant (member removes themselves)
 */
export async function leaveTenant(tenantId: string): Promise<void> {
  await apiPost<void>(
    API_PATHS.TENANTS.leave(tenantId),
    {},
    { ...tenantOptions, skipJsonParse: true },
  );
}

/**
 * Delete a tenant (owner only)
 */
export async function deleteTenant(tenantId: string): Promise<void> {
  await apiDelete(API_PATHS.TENANTS.delete(tenantId), tenantOptions);
}

/**
 * Everything Orkyo stores about the signed-in person, across every organization (GDPR
 * access/portability). Returned as the parsed JSON document; the caller offers it as a file.
 */
export async function exportPersonalData(): Promise<unknown> {
  return apiGet<unknown>(API_PATHS.ACCOUNT.EXPORT, tenantOptions);
}

/**
 * Permanently delete the signed-in person's account and data. `confirmEmail` must match the
 * account's email; a 409 names the organizations that must be handed over or deleted first.
 */
export async function deleteOwnAccount(confirmEmail: string): Promise<void> {
  await apiPost<void>(
    API_PATHS.ACCOUNT.DELETE,
    { confirmEmail },
    { ...tenantOptions, skipJsonParse: true },
  );
}

/**
 * Cancel a pending tenant deletion (owner only, during grace period)
 */
export async function cancelTenantDeletion(tenantId: string): Promise<void> {
  await apiPost<void>(
    API_PATHS.TENANTS.cancelDeletion(tenantId),
    {},
    { ...tenantOptions, skipJsonParse: true },
  );
}

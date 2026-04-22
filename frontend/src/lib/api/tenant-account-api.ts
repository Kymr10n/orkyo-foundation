/**
 * API client for Tenant Management operations
 *
 * These endpoints are called without tenant context (before tenant selection)
 * and require only OIDC authentication.
 */

import { TENANT_HEADER_NAME } from "@/constants/http";
import { apiGet, apiPost, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface TenantMembership {
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

interface CreateTenantRequest {
  slug: string;
  displayName: string;
  starterTemplate?: string;
}

interface CreateTenantResponse {
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

/**
 * Check if the current user can create a new tenant
 */
export async function canCreateTenant(): Promise<CanCreateTenantResponse> {
  return apiGet<CanCreateTenantResponse>(API_PATHS.TENANTS.CAN_CREATE, tenantOptions);
}

/**
 * Create a new tenant
 */
export async function createTenant(request: CreateTenantRequest): Promise<CreateTenantResponse> {
  return apiPost<CreateTenantResponse>(
    API_PATHS.TENANTS.CREATE,
    request,
    tenantOptions,
  );
}

/**
 * Get available starter templates for new tenant creation
 */
export async function getStarterTemplates(): Promise<StarterTemplateInfo[]> {
  return apiGet<StarterTemplateInfo[]>(API_PATHS.TENANTS.STARTER_TEMPLATES, tenantOptions);
}

/**
 * Get all tenant memberships for the current user
 */
export async function getTenantMemberships(): Promise<TenantMembership[]> {
  return apiGet<TenantMembership[]>(API_PATHS.TENANTS.MEMBERSHIPS, tenantOptions);
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
 * Cancel a pending tenant deletion (owner only, during grace period)
 */
export async function cancelTenantDeletion(tenantId: string): Promise<void> {
  await apiPost<void>(
    API_PATHS.TENANTS.cancelDeletion(tenantId),
    {},
    { ...tenantOptions, skipJsonParse: true },
  );
}

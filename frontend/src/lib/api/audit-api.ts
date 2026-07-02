/**
 * Tenant-admin audit log API client — GET /api/audit.
 *
 * Tenant-scoped (only the current tenant's events) and gated behind the `audit_log`
 * feature (Professional+ in SaaS; always on in Community). Sensitive fields
 * (ip_address, request_id) are intentionally not returned by the endpoint.
 */

import { apiGet } from '../core/api-client';
import { API_BASE_URL } from '../core/api-utils';

export interface TenantAuditEvent {
  id: string;
  actorUserId: string | null;
  actorEmail: string | null;
  actorDisplayName: string | null;
  actorType: string; // 'user' | 'system' | 'api'
  action: string;
  targetType: string | null;
  targetId: string | null;
  metadata: string | null; // JSON string
  createdAt: string;
}

export interface AuditEventPage {
  events: TenantAuditEvent[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuditFilters {
  action?: string;
  actorId?: string;
  targetType?: string;
  from?: string;
  to?: string;
  page?: number; // 1-based (backend)
  pageSize?: number;
}

const BASE = `${API_BASE_URL}/api/audit/`;

export async function getTenantAuditEvents(filters?: AuditFilters): Promise<AuditEventPage> {
  const params: Record<string, string | number> = {};
  if (filters?.action) params.action = filters.action;
  if (filters?.actorId) params.actorId = filters.actorId;
  if (filters?.targetType) params.targetType = filters.targetType;
  if (filters?.from) params.from = filters.from;
  if (filters?.to) params.to = filters.to;
  if (filters?.page !== undefined) params.page = filters.page;
  if (filters?.pageSize !== undefined) params.pageSize = filters.pageSize;
  return apiGet<AuditEventPage>(BASE, { params });
}

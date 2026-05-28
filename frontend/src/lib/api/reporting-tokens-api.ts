/**
 * Reporting Token Admin API
 *
 * Manages reporting API tokens for external BI tool integrations.
 * Tenant-admin only.
 */

import { apiGet, apiPost, apiDelete } from '../core/api-client';

export interface ReportingTokenSummary {
  id: string;
  tenantId: string;
  name: string;
  tokenPrefix: string;
  scopes: string;
  createdAtUtc: string;
  createdByUserId: string | null;
  lastUsedAtUtc: string | null;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  isActive: boolean;
}

export interface CreatedReportingToken {
  summary: ReportingTokenSummary;
  /** Full token string — shown once, never again. */
  rawToken: string;
}

export interface CreateReportingTokenRequest {
  name: string;
  expiresAt?: string;
}

export async function listReportingTokens(): Promise<ReportingTokenSummary[]> {
  return apiGet<ReportingTokenSummary[]>('/api/reporting/v1/tokens');
}

export async function createReportingToken(
  req: CreateReportingTokenRequest
): Promise<CreatedReportingToken> {
  return apiPost<CreatedReportingToken>('/api/reporting/v1/tokens', req);
}

export async function revokeReportingToken(tokenId: string): Promise<void> {
  return apiDelete(`/api/reporting/v1/tokens/${tokenId}`);
}

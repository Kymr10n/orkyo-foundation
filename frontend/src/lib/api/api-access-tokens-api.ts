/**
 * API Access Token Admin API
 *
 * Manages the write-capable, per-tenant credentials an MCP client (or any automated service)
 * authenticates with. Tenant-admin only.
 *
 * Deliberately separate from the reporting-token client: the two are different trust classes on
 * the server, and collapsing them here would hide that a token issued from this surface can change
 * the schedule.
 */

import { apiGet, apiPost, apiDelete } from '../core/api-client';

/** Scopes a token can carry. Mirrors PlatformApiScopes on the server. */
export const API_SCOPES = {
  scheduleRead: 'schedule:read',
  scheduleWrite: 'schedule:write',
} as const;

export type ApiScope = (typeof API_SCOPES)[keyof typeof API_SCOPES];

export interface ApiAccessTokenSummary {
  id: string;
  tenantId: string;
  name: string;
  tokenPrefix: string;
  /** Space-delimited, as stored. */
  scopes: string;
  createdAtUtc: string;
  createdByUserId: string | null;
  lastUsedAtUtc: string | null;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  isActive: boolean;
}

export interface CreatedApiAccessToken {
  summary: ApiAccessTokenSummary;
  /** Full token string — shown once, never again. */
  rawToken: string;
}

export interface CreateApiAccessTokenRequest {
  name: string;
  scopes: ApiScope[];
  expiresAt?: string;
}

export async function listApiAccessTokens(): Promise<ApiAccessTokenSummary[]> {
  return apiGet<ApiAccessTokenSummary[]>('/api/platform/v1/tokens');
}

export async function createApiAccessToken(
  req: CreateApiAccessTokenRequest
): Promise<CreatedApiAccessToken> {
  return apiPost<CreatedApiAccessToken>('/api/platform/v1/tokens', req);
}

export async function revokeApiAccessToken(tokenId: string): Promise<void> {
  return apiDelete(`/api/platform/v1/tokens/${tokenId}`);
}

/** True when the token can change the schedule — drives the write-access warning in the UI. */
export function grantsWrite(scopes: string): boolean {
  return scopes.split(' ').includes(API_SCOPES.scheduleWrite);
}

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { listReportingTokens, createReportingToken, revokeReportingToken } from './reporting-tokens-api';
import * as apiClient from '../core/api-client';

vi.mock('../core/api-client');

const mockToken = {
  id: 'tok-1', tenantId: 't1', name: 'Power BI', tokenPrefix: 'abc',
  scopes: 'reporting:read', createdAtUtc: '2026-01-01T00:00:00Z',
  createdByUserId: null, lastUsedAtUtc: null, expiresAtUtc: null,
  revokedAtUtc: null, isActive: true,
};

describe('reporting-tokens-api', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('listReportingTokens', () => {
    it('calls apiGet on the tokens endpoint and returns list', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockToken]);
      const result = await listReportingTokens();
      expect(apiClient.apiGet).toHaveBeenCalledWith('/api/reporting/v1/tokens');
      expect(result).toEqual([mockToken]);
    });
  });

  describe('createReportingToken', () => {
    it('calls apiPost with name and optional expiry', async () => {
      const req = { name: 'My Token', expiresAt: '2027-01-01T00:00:00Z' };
      const mockCreated = { summary: mockToken, rawToken: 'secret' };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockCreated);
      const result = await createReportingToken(req);
      expect(apiClient.apiPost).toHaveBeenCalledWith('/api/reporting/v1/tokens', req);
      expect(result.rawToken).toBe('secret');
    });

    it('calls apiPost without expiry when not provided', async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue({ summary: mockToken, rawToken: 'x' });
      await createReportingToken({ name: 'No expiry' });
      expect(apiClient.apiPost).toHaveBeenCalledWith('/api/reporting/v1/tokens', { name: 'No expiry' });
    });
  });

  describe('revokeReportingToken', () => {
    it('calls apiDelete on the token-specific endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
      await revokeReportingToken('tok-1');
      expect(apiClient.apiDelete).toHaveBeenCalledWith('/api/reporting/v1/tokens/tok-1');
    });
  });
});

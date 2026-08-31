import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../core/api-client', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));

import { apiGet, apiPost, apiDelete } from '../core/api-client';
import {
  listApiAccessTokens,
  createApiAccessToken,
  revokeApiAccessToken,
  grantsWrite,
  API_SCOPES,
} from './api-access-tokens-api';

beforeEach(() => vi.clearAllMocks());

describe('api-access-tokens-api', () => {
  it('reads tokens from the platform surface, not the reporting one', async () => {
    // The two credential classes have separate endpoints because they are separate trust classes;
    // pointing this client at /api/reporting would quietly issue read-only tokens instead.
    vi.mocked(apiGet).mockResolvedValue([]);

    await listApiAccessTokens();

    expect(apiGet).toHaveBeenCalledWith('/api/platform/v1/tokens');
  });

  it('posts the requested scopes when creating a token', async () => {
    vi.mocked(apiPost).mockResolvedValue({ summary: {}, rawToken: 'orkyo_api_x_y' });

    await createApiAccessToken({
      name: 'agent',
      scopes: [API_SCOPES.scheduleRead, API_SCOPES.scheduleWrite],
    });

    expect(apiPost).toHaveBeenCalledWith('/api/platform/v1/tokens', {
      name: 'agent',
      scopes: ['schedule:read', 'schedule:write'],
    });
  });

  it('revokes by id', async () => {
    vi.mocked(apiDelete).mockResolvedValue(undefined);

    await revokeApiAccessToken('tok-1');

    expect(apiDelete).toHaveBeenCalledWith('/api/platform/v1/tokens/tok-1');
  });
});

describe('grantsWrite', () => {
  it('is true only when the write scope is actually present', () => {
    expect(grantsWrite('schedule:read schedule:write')).toBe(true);
    expect(grantsWrite('schedule:write')).toBe(true);
    expect(grantsWrite('schedule:read')).toBe(false);
    expect(grantsWrite('')).toBe(false);
  });

  it('does not match a scope that merely contains the write scope as a substring', () => {
    // A prefix match would make "schedule:write-nothing" read as write access.
    expect(grantsWrite('schedule:write-nothing')).toBe(false);
  });
});

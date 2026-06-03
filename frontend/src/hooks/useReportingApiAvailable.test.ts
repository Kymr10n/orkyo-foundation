import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useReportingApiAvailable } from '@foundation/src/hooks/useReportingApiAvailable';
import { SERVICE_TIER } from '@foundation/src/lib/api/admin-api';

const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({ useAuth: mockUseAuth }));

/** Build a useAuth() return value with sensible defaults. */
function authState(
  overrides: { tier?: string; isBreakGlass?: boolean; isSiteAdmin?: boolean; membership?: unknown } = {},
) {
  const { tier, isBreakGlass, isSiteAdmin = false, membership } = overrides;
  return {
    isSiteAdmin,
    membership:
      membership !== undefined
        ? membership
        : { tier: tier ?? SERVICE_TIER.FREE, isBreakGlass: isBreakGlass ?? false },
  };
}

describe('useReportingApiAvailable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns true for site admins regardless of tier', () => {
    mockUseAuth.mockReturnValue(authState({ isSiteAdmin: true, tier: SERVICE_TIER.FREE }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(true);
  });

  it('returns true for break-glass memberships (no tier field)', () => {
    mockUseAuth.mockReturnValue(authState({ membership: { isBreakGlass: true } }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(true);
  });

  it('returns false for Free tier', () => {
    mockUseAuth.mockReturnValue(authState({ tier: SERVICE_TIER.FREE }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(false);
  });

  it('returns true for Professional tier', () => {
    mockUseAuth.mockReturnValue(authState({ tier: SERVICE_TIER.PROFESSIONAL }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(true);
  });

  it('returns true for Enterprise tier', () => {
    mockUseAuth.mockReturnValue(authState({ tier: SERVICE_TIER.ENTERPRISE }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(true);
  });

  it('returns false when membership is null (unauthenticated)', () => {
    mockUseAuth.mockReturnValue(authState({ membership: null }));
    const { result } = renderHook(() => useReportingApiAvailable());
    expect(result.current).toBe(false);
  });
});

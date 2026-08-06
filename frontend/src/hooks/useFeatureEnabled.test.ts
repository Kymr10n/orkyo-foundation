import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useFeatureEnabled } from '@foundation/src/hooks/useFeatureEnabled';
import { FeatureKeys, PlanCodes } from '@foundation/contracts/plans';

const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({ useAuth: mockUseAuth }));

/** Build a useAuth() return value with sensible defaults. */
function authState(
  overrides: {
    entitlements?: Record<string, boolean>;
    isBreakGlass?: boolean;
    isSiteAdmin?: boolean;
    membership?: unknown;
  } = {},
) {
  const { entitlements, isBreakGlass, isSiteAdmin = false, membership } = overrides;
  return {
    isSiteAdmin,
    membership:
      membership !== undefined
        ? membership
        : { entitlements: entitlements ?? {}, isBreakGlass: isBreakGlass ?? false },
  };
}

describe('useFeatureEnabled', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns true when the server reports the feature as entitled', () => {
    mockUseAuth.mockReturnValue(authState({ entitlements: { [FeatureKeys.CalendarFeed]: true } }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(true);
  });

  it('returns false when the server reports the feature as not entitled', () => {
    mockUseAuth.mockReturnValue(authState({ entitlements: { [FeatureKeys.CalendarFeed]: false } }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(false);
  });

  it('fails closed when the key is absent (older backend, unresolved tenant)', () => {
    mockUseAuth.mockReturnValue(authState({ entitlements: { [FeatureKeys.AuditLog]: true } }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(false);
  });

  it('fails closed when the session carries no entitlements at all', () => {
    mockUseAuth.mockReturnValue(authState({ membership: { isBreakGlass: false } }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(false);
  });

  it('returns false when membership is null (unauthenticated)', () => {
    mockUseAuth.mockReturnValue(authState({ membership: null }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(false);
  });

  it('returns true for site admins regardless of entitlements', () => {
    mockUseAuth.mockReturnValue(
      authState({ isSiteAdmin: true, entitlements: { [FeatureKeys.CalendarFeed]: false } }),
    );
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(true);
  });

  it('returns true for break-glass memberships (which carry no entitlements)', () => {
    mockUseAuth.mockReturnValue(authState({ membership: { isBreakGlass: true } }));
    const { result } = renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed));
    expect(result.current).toBe(true);
  });

  it('ignores the plan code entirely — entitlements are the only signal', () => {
    // The regression: /me used to send the display label ("Enterprise"), which failed a
    // string compare against lowercase codes and locked the feature for paying tenants.
    // Gating no longer looks at the plan at all, so neither spelling can influence it.
    mockUseAuth.mockReturnValue(
      authState({ membership: { tier: 'Enterprise', entitlements: { [FeatureKeys.CalendarFeed]: true } } }),
    );
    expect(renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed)).result.current).toBe(true);

    mockUseAuth.mockReturnValue(
      authState({ membership: { tier: PlanCodes.Enterprise, entitlements: { [FeatureKeys.CalendarFeed]: false } } }),
    );
    expect(renderHook(() => useFeatureEnabled(FeatureKeys.CalendarFeed)).result.current).toBe(false);
  });
});

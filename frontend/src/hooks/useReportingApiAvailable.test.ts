import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useReportingApiAvailable } from '@foundation/src/hooks/useReportingApiAvailable';
import { FeatureKeys } from '@foundation/contracts/plans';

const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({ useAuth: mockUseAuth }));

/**
 * Reporting API access delegates to useFeatureEnabled — the exhaustive cases (site admin,
 * break-glass, absent key) live in useFeatureEnabled.test.ts. These pin the wiring:
 * this hook must read THIS feature key, and must not consult the plan code.
 */
function authState(entitlements: Record<string, boolean>, membership?: unknown) {
  return {
    isSiteAdmin: false,
    membership: membership !== undefined ? membership : { entitlements, isBreakGlass: false },
  };
}

describe('useReportingApiAvailable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns true when the server entitles ApiAccess', () => {
    mockUseAuth.mockReturnValue(authState({ [FeatureKeys.ApiAccess]: true }));
    expect(renderHook(() => useReportingApiAvailable()).result.current).toBe(true);
  });

  it('returns false when the server does not entitle ApiAccess', () => {
    mockUseAuth.mockReturnValue(authState({ [FeatureKeys.ApiAccess]: false }));
    expect(renderHook(() => useReportingApiAvailable()).result.current).toBe(false);
  });

  it('reads its own feature key, not a neighbouring one', () => {
    mockUseAuth.mockReturnValue(authState({ [FeatureKeys.CalendarFeed]: true }));
    expect(renderHook(() => useReportingApiAvailable()).result.current).toBe(false);
  });

  it('is not influenced by the plan code, in either spelling', () => {
    // Regression: the session used to carry the display label ("Enterprise"), which failed
    // a compare against lowercase codes and locked this feature for paying tenants.
    mockUseAuth.mockReturnValue(
      authState({}, { tier: 'Enterprise', entitlements: { [FeatureKeys.ApiAccess]: true } }),
    );
    expect(renderHook(() => useReportingApiAvailable()).result.current).toBe(true);

    mockUseAuth.mockReturnValue(
      authState({}, { tier: 'enterprise', entitlements: { [FeatureKeys.ApiAccess]: false } }),
    );
    expect(renderHook(() => useReportingApiAvailable()).result.current).toBe(false);
  });
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";

// Test the real hook, not the global test-mock from src/test/setup.ts.
vi.unmock("@foundation/src/hooks/usePermissions");

vi.mock('@foundation/src/contexts/AuthContext', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  useAuth: vi.fn(),
}));

import { useCanEdit, useIsTenantAdmin } from "@foundation/src/hooks/usePermissions";
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { mockAuth } from '@foundation/src/test-utils/auth';

interface AuthOverrides {
  membership?: { role?: string; isTenantAdmin?: boolean } | null;
  isSiteAdmin?: boolean;
}
// No membership unless the test names one; a named one is not a tenant admin unless it says so.
const auth = (o: AuthOverrides = {}) =>
  mockAuth({
    membership: o.membership ? { isTenantAdmin: false, ...o.membership } : null,
    isSiteAdmin: o.isSiteAdmin ?? false,
  });

beforeEach(() => vi.mocked(useAuth).mockReset());

describe("useCanEdit", () => {
  it("is true for an editor", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "editor" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is true for an admin", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "admin" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is case-insensitive on the role string", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "EDITOR" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is false for a viewer", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "viewer" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(false);
  });

  it("is false when there is no membership", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: null }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(false);
  });

  it("is true for a site admin regardless of tenant role", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "viewer" }, isSiteAdmin: true }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is true when the membership is flagged tenant admin", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "viewer", isTenantAdmin: true } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });
});

describe("useIsTenantAdmin", () => {
  it("is true when the membership is flagged tenant admin", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "admin", isTenantAdmin: true } }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(true);
  });

  it("is true for a site admin", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ isSiteAdmin: true }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(true);
  });

  it("is false for an ordinary editor", () => {
    vi.mocked(useAuth).mockReturnValue(auth({ membership: { role: "editor" } }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(false);
  });
});

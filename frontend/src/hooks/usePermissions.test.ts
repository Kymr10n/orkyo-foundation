import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";

// Test the real hook, not the global test-mock from src/test/setup.ts.
vi.unmock("@foundation/src/hooks/usePermissions");

const mockUseAuth = vi.fn();
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

import { useCanEdit, useIsTenantAdmin } from "@foundation/src/hooks/usePermissions";

interface AuthOverrides {
  membership?: { role?: string; isTenantAdmin?: boolean } | null;
  isSiteAdmin?: boolean;
}
const auth = (o: AuthOverrides = {}) => ({
  membership: o.membership ?? null,
  isSiteAdmin: o.isSiteAdmin ?? false,
});

beforeEach(() => mockUseAuth.mockReset());

describe("useCanEdit", () => {
  it("is true for an editor", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "editor" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is true for an admin", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "admin" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is case-insensitive on the role string", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "EDITOR" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is false for a viewer", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "viewer" } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(false);
  });

  it("is false when there is no membership", () => {
    mockUseAuth.mockReturnValue(auth({ membership: null }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(false);
  });

  it("is true for a site admin regardless of tenant role", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "viewer" }, isSiteAdmin: true }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });

  it("is true when the membership is flagged tenant admin", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "viewer", isTenantAdmin: true } }));
    expect(renderHook(() => useCanEdit()).result.current).toBe(true);
  });
});

describe("useIsTenantAdmin", () => {
  it("is true when the membership is flagged tenant admin", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "admin", isTenantAdmin: true } }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(true);
  });

  it("is true for a site admin", () => {
    mockUseAuth.mockReturnValue(auth({ isSiteAdmin: true }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(true);
  });

  it("is false for an ordinary editor", () => {
    mockUseAuth.mockReturnValue(auth({ membership: { role: "editor" } }));
    expect(renderHook(() => useIsTenantAdmin()).result.current).toBe(false);
  });
});

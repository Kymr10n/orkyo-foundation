/**
 * Regression test: ApexGateway uses `useNavigate`, so it must be rendered
 * inside a Router. Without one, `useNavigate` throws a bare Error with no
 * message — hard to debug in minified production builds.
 *
 * This foundation-level test verifies the Router invariant on ApexGateway
 * itself, without requiring any composition-layer pages. The original
 * failure was triggered through SaaS's AdminPage (which also uses
 * `useNavigate`); coverage of the full SaaS-injected admin path lives in
 * the orkyo-saas test suite where AdminPage is owned.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AUTH_STAGES } from "@/constants/auth";

const mockUseAuth = vi.fn();
vi.mock("@/contexts/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

import { ApexGateway } from "./ApexGateway";

describe("ApexGateway — Router regression", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(window, "location", {
      value: {
        pathname: "/admin",
        hostname: "orkyo.com",
        protocol: "https:",
        href: "",
      },
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders inside a Router without throwing", async () => {
    mockUseAuth.mockReturnValue({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [] },
      isSiteAdmin: true,
      canAccessAdminPage: true,
      appUser: { displayName: "Admin", email: "admin@test.com" },
      send: vi.fn(),
    });

    expect(() => {
      render(
        <MemoryRouter>
          <ApexGateway />
        </MemoryRouter>,
      );
    }).not.toThrow();

    await act(async () => {});
  });

  it("throws when rendered WITHOUT a Router (documents the invariant)", () => {
    mockUseAuth.mockReturnValue({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [] },
      isSiteAdmin: true,
      canAccessAdminPage: true,
      appUser: { displayName: "Admin", email: "admin@test.com" },
      send: vi.fn(),
    });

    // Suppress console.error from React's error boundary.
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});

    expect(() => {
      render(<ApexGateway />);
    }).toThrow();

    spy.mockRestore();
  });
});

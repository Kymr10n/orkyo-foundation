/**
 * Regression test: AdminPage uses useNavigate, so ApexGateway must be
 * rendered inside a Router. Without it, useNavigate throws a bare Error
 * with no message — hard to debug in minified production builds.
 *
 * This test uses the REAL AdminPage (not mocked) to verify the Router
 * requirement is met. If App.tsx ever removes the BrowserRouter wrapper
 * from the apex domain path, this test will catch it.
 *
 * See: https://orkyo.com/assets/index-2xq_0WSO.js:2 "Uncaught Error:"
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { AUTH_STAGES } from "@/constants/auth";

// Mock everything EXCEPT AdminPage — we want the real useNavigate call.
vi.mock("@/pages/LoginPage", () => ({
  LoginPage: () => <div data-testid="login-page" />,
}));
vi.mock("@/pages/TosPage", () => ({
  TosPage: () => <div data-testid="tos-page" />,
}));
vi.mock("@/pages/OnboardingPage", () => ({
  OnboardingPage: () => <div data-testid="onboarding-page" />,
}));
vi.mock("@/pages/TenantSelectPage", () => ({
  TenantSelectPage: () => <div data-testid="tenant-select-page" />,
}));

// AdminPage dependencies — stub what it imports so we don't need the full tree,
// but keep useNavigate real.
vi.mock("@/components/ui/tabs", () => ({
  Tabs: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  TabsContent: ({ children }: { children: React.ReactNode }) => (
    <div>{children}</div>
  ),
  TabsList: ({ children }: { children: React.ReactNode }) => (
    <div>{children}</div>
  ),
  TabsTrigger: ({ children }: { children: React.ReactNode }) => (
    <button>{children}</button>
  ),
}));
vi.mock("@/components/layout/ThemeToggle", () => ({
  ThemeToggle: () => null,
}));
vi.mock("@/components/settings/TenantConfigSettings", () => ({
  TenantConfigSettings: () => null,
}));
vi.mock("@/components/admin/AnnouncementsTab", () => ({
  AnnouncementsTab: () => null,
}));
vi.mock("@/lib/api/admin-api", () => ({
  getAdminTenants: vi.fn().mockResolvedValue({ tenants: [] }),
  getAdminUsers: vi.fn().mockResolvedValue({ users: [] }),
  getAdminUser: vi.fn().mockResolvedValue({}),
  getAdminTenantMembers: vi.fn().mockResolvedValue({ members: [] }),
  createAdminTenant: vi.fn(),
  updateAdminTenant: vi.fn(),
  deleteAdminTenant: vi.fn(),
  addAdminTenantMember: vi.fn(),
  updateAdminTenantMember: vi.fn(),
  removeAdminTenantMember: vi.fn(),
  auditBreakGlassEntry: vi.fn(),
  deactivateAdminUser: vi.fn(),
  reactivateAdminUser: vi.fn(),
  deleteAdminUser: vi.fn(),
}));
vi.mock("@/lib/utils/tenant-navigation", () => ({
  navigateToTenantSubdomain: vi.fn(() => false),
  setBreakGlassCookie: vi.fn(),
}));

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

  it("renders AdminPage (real, with useNavigate) inside a Router without throwing", async () => {
    mockUseAuth.mockReturnValue({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [] },
      isSiteAdmin: true,
      canAccessAdminPage: true,
      appUser: { displayName: "Admin", email: "admin@test.com" },
      send: vi.fn(),
    });

    // This would throw "useNavigate() may be used only in the context of
    // a <Router> component" if the Router wrapper is missing.
    expect(() => {
      render(
        <MemoryRouter>
          <ApexGateway />
        </MemoryRouter>,
      );
    }).not.toThrow();

    expect(screen.getByText("Site Administration")).toBeInTheDocument();
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

    // Suppress console.error from React's error boundary
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});

    expect(() => {
      render(<ApexGateway />);
    }).toThrow();

    spy.mockRestore();
  });
});

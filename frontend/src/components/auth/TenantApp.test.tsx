import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type * as ReactRouterDom from "react-router-dom";
import { AUTH_STAGES, AUTH_EVENTS, TENANT_STATUS } from "@/constants/auth";

// ── Mock page components ────────────────────────────────────────────────

// AppLayout uses <Outlet> for nested routes — replicate that in the mock
vi.mock("@/components/layout/AppLayout", async () => {
  const { Outlet } =
    await vi.importActual<typeof ReactRouterDom>(
      "react-router-dom",
    );
  return {
    AppLayout: () => (
      <div data-testid="app-layout">
        <Outlet />
      </div>
    ),
  };
});
vi.mock("@/pages/LoginPage", () => ({
  LoginPage: () => <div data-testid="login-page">Login</div>,
}));
vi.mock("@/pages/AboutPage", () => ({
  AboutPage: () => <div data-testid="about-page">About</div>,
}));
vi.mock("@/pages/AccountPage", () => ({
  AccountPage: () => <div data-testid="account-page">Account</div>,
}));
vi.mock("@/pages/AdminPage", () => ({
  AdminPage: () => <div data-testid="admin-page">Admin</div>,
}));
vi.mock("@/pages/UtilizationPage", () => ({
  UtilizationPage: () => <div data-testid="utilization-page">Utilization</div>,
}));
vi.mock("@/pages/SpacesPage", () => ({
  SpacesPage: () => <div data-testid="spaces-page">Spaces</div>,
}));
vi.mock("@/pages/ConflictsPage", () => ({
  ConflictsPage: () => <div data-testid="conflicts-page">Conflicts</div>,
}));
vi.mock("@/pages/RequestsPage", () => ({
  RequestsPage: () => <div data-testid="requests-page">Requests</div>,
}));
vi.mock("@/pages/SettingsPage", () => ({
  SettingsPage: () => <div data-testid="settings-page">Settings</div>,
}));
vi.mock("@/pages/MessagesPage", () => ({
  MessagesPage: () => <div data-testid="messages-page">Messages</div>,
}));
vi.mock("@/components/layout/ThemeToggle", () => ({
  ThemeToggle: () => null,
}));
vi.mock("@/pages/TenantSuspendedPage", () => ({
  TenantSuspendedPage: () => <div data-testid="tenant-suspended-page">Suspended</div>,
}));

// ── Mock RequireAuth — pass through children ────────────────────────────

vi.mock("@/components/auth/RequireAuth", () => ({
  RequireAuth: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

// ── Mock useAuth ────────────────────────────────────────────────────────

const mockSend = vi.fn();
const mockUseAuth = vi.fn();

vi.mock("@/contexts/AuthContext", () => ({
  useAuth: () => mockUseAuth(),
}));

// ── Mock tenant navigation (legacy — no longer used by TenantApp) ────────

vi.mock("@/lib/utils/tenant-navigation", () => ({
  redirectToLogin: vi.fn(),
}));

import { TenantApp } from "./TenantApp";

// ── Helpers ─────────────────────────────────────────────────────────────

function authState(overrides: Record<string, unknown> = {}) {
  return {
    authStage: AUTH_STAGES.READY,
    send: mockSend,
    ...overrides,
  };
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <TenantApp />
    </MemoryRouter>,
  );
}

// ── Tests ───────────────────────────────────────────────────────────────

describe("TenantApp", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue(authState());
  });

  // ── Route rendering ───────────────────────────────────────────────────

  it.each([
    ["/",         "app-layout"],
    ["/about",    "about-page"],
    ["/account",  "account-page"],
    ["/admin",    "admin-page"],
    ["/messages", "messages-page"],
    ["/spaces",   "spaces-page"],
    ["/requests", "requests-page"],
    ["/conflicts","conflicts-page"],
    ["/settings", "settings-page"],
    ["/login",    "login-page"],
  ])("renders %s", async (path, testId) => {
    renderAt(path);
    expect(await screen.findByTestId(testId)).toBeInTheDocument();
  });

  // ── Unauthenticated redirect ──────────────────────────────────────────

  it("sends LOGIN to the machine when unauthenticated (triggers BFF redirect)", async () => {
    mockUseAuth.mockReturnValue(
      authState({
        authStage: AUTH_STAGES.UNAUTHENTICATED,
      }),
    );

    renderAt("/");

    await waitFor(() => {
      expect(mockSend).toHaveBeenCalledWith({ type: AUTH_EVENTS.LOGIN });
    });
  });

  it("does not send LOGIN while initializing", () => {
    mockUseAuth.mockReturnValue(
      authState({
        authStage: AUTH_STAGES.INITIALIZING,
      }),
    );

    renderAt("/");

    expect(mockSend).not.toHaveBeenCalledWith({ type: AUTH_EVENTS.LOGIN });
  });

  // ── Suspended tenant on subdomain ───────────────────────────────────────

  it("renders TenantSuspendedPage when selecting_tenant with suspended membership", () => {
    mockUseAuth.mockReturnValue(
      authState({
        authStage: AUTH_STAGES.SELECTING_TENANT,
        membership: {
          slug: "the-goo-factory",
          state: TENANT_STATUS.SUSPENDED,
          canReactivate: true,
          suspensionReason: "inactivity",
        },
      }),
    );

    renderAt("/");

    expect(screen.getByTestId("tenant-suspended-page")).toBeInTheDocument();
  });

  it("does not render TenantSuspendedPage when selecting_tenant without suspended membership", () => {
    mockUseAuth.mockReturnValue(
      authState({
        authStage: AUTH_STAGES.SELECTING_TENANT,
        membership: null,
      }),
    );

    renderAt("/");

    expect(screen.queryByTestId("tenant-suspended-page")).not.toBeInTheDocument();
  });

  it("does not render TenantSuspendedPage when selecting_tenant with active membership", () => {
    mockUseAuth.mockReturnValue(
      authState({
        authStage: AUTH_STAGES.SELECTING_TENANT,
        membership: {
          slug: "acme",
          state: TENANT_STATUS.ACTIVE,
        },
      }),
    );

    renderAt("/");

    expect(screen.queryByTestId("tenant-suspended-page")).not.toBeInTheDocument();
  });
});

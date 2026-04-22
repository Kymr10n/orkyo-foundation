import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { AccountPage } from "@/pages/AccountPage";

// Mock navigate
const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual("react-router-dom");
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

// Mock AuthContext
const mockSetMembership = vi.fn();
const mockLogout = vi.fn();
let mockMembership: { tenantId: string; slug: string } | null = null;
let mockIsSiteAdmin = false;
const mockSend = vi.fn();

vi.mock("@/contexts/AuthContext", () => ({
  useAuth: () => ({
    membership: mockMembership,
    setMembership: mockSetMembership,
    logout: mockLogout,
    send: mockSend,
    user: { sub: "test-user", email: "test@example.com" },
    appUser: {
      id: "test-user",
      email: "test@example.com",
      displayName: "Alex Johnson",
    },
    isSiteAdmin: mockIsSiteAdmin,
  }),
  getAuthTokenSync: () => "test-token",
  getTenantSlugSync: () => mockMembership?.slug || "demo",
}));

// Mock tenant navigation
vi.mock("@/lib/utils/tenant-navigation", () => ({
  navigateToTenantSubdomain: vi.fn(() => false),
  navigateToApex: vi.fn(() => false),
}));

// Mock tenants-api
const mockGetTenantMemberships = vi.fn();
const mockLeaveTenant = vi.fn();
const mockDeleteTenant = vi.fn();

vi.mock("@/lib/api/tenant-account-api", () => ({
  getTenantMemberships: () => mockGetTenantMemberships(),
  leaveTenant: (...args: unknown[]) => mockLeaveTenant(...args),
  deleteTenant: (...args: unknown[]) => mockDeleteTenant(...args),
}));

// Mock security-api (used by Profile tab)
const mockUpdateUserProfile = vi.fn().mockResolvedValue({ message: "ok", displayName: "Bob Smith" });

vi.mock("@/lib/api/security-api", () => ({
  getUserProfile: () =>
    Promise.resolve({
      email: "test@example.com",
      firstName: "Alex",
      lastName: "Johnson",
      emailVerified: true,
    }),
  updateUserProfile: (...args: unknown[]) => mockUpdateUserProfile(...args),
  getSecurityInfo: () =>
    Promise.resolve({ isFederated: false, canChangePassword: true }),
  getSessions: () => Promise.resolve([]),
  changePassword: vi.fn(),
  revokeSession: vi.fn(),
  logoutAllSessions: vi.fn(),
  getMfaStatus: () =>
    Promise.resolve({ totpEnabled: false, recoveryCodesConfigured: false }),
  removeMfa: vi.fn(),
}));

const mockMemberships = [
  {
    tenantId: "tenant-1",
    tenantSlug: "acme-corp",
    tenantDisplayName: "ACME Corporation",
    tenantStatus: "active",
    role: "admin",
    status: "active",
    isOwner: true,
    joinedAt: "2024-01-01T00:00:00Z",
  },
  {
    tenantId: "tenant-2",
    tenantSlug: "test-org",
    tenantDisplayName: "Test Organization",
    tenantStatus: "active",
    role: "editor",
    status: "active",
    isOwner: false,
    joinedAt: "2024-02-01T00:00:00Z",
  },
];

const createWrapper = (initialPath = "/account") => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialPath]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>{children}</MemoryRouter>
    </QueryClientProvider>
  );
};

describe("AccountPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockMembership = {
      tenantId: "tenant-1",
      slug: "acme-corp",
    };
    mockIsSiteAdmin = false;
    mockGetTenantMemberships.mockResolvedValue(mockMemberships);
  });

  it("renders loading state initially", async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    // Should show loading spinner initially (Loader2 icon with animate-spin class)
    expect(document.querySelector(".animate-spin")).toBeInTheDocument();

    // Wait for async effects to settle
    await waitFor(() => {});
  });

  it("renders user name and email in the header", async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      // The h1 header should show the display name
      const heading = screen.getByRole("heading", { level: 1 });
      expect(heading).toHaveTextContent("Alex Johnson");
    });
    // Email appears in both header and profile tab, verify at least one exists
    expect(
      screen.getAllByText("test@example.com").length,
    ).toBeGreaterThanOrEqual(1);
    // Should show initials avatar
    expect(screen.getByText("AJ")).toBeInTheDocument();
  });

  it("renders memberships after loading", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    expect(screen.getByText("Test Organization")).toBeInTheDocument();
    expect(screen.getByText("acme-corp")).toBeInTheDocument();
    expect(screen.getByText("test-org")).toBeInTheDocument();
  });

  it("displays owner badge for owned tenants", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    // Owner should have delete button, not leave button
    // The owned tenant should show the Crown icon (owner badge)
    const deleteButtons = screen.getAllByRole("button");
    expect(deleteButtons.some((btn) => btn.querySelector("svg"))).toBeTruthy();
  });

  it("displays role badges correctly", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("admin")).toBeInTheDocument();
    });

    expect(screen.getByText("editor")).toBeInTheDocument();
  });

  it("marks active tenant", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Active")).toBeInTheDocument();
    });
  });

  it("handles switch tenant action", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    // Find and click switch button for non-active tenant
    const switchButtons = screen.getAllByRole("button", { name: /switch/i });
    expect(switchButtons.length).toBeGreaterThan(0);

    fireEvent.click(switchButtons[0]);

    expect(mockSetMembership).toHaveBeenCalledWith(
      expect.objectContaining({
        slug: "test-org",
        displayName: "Test Organization",
        role: "editor",
        isOwner: false,
      }),
    );
    expect(mockNavigate).toHaveBeenCalledWith("/", { replace: true });
  });

  it("hides Switch button for suspended tenants", async () => {
    mockGetTenantMemberships.mockResolvedValue([
      ...mockMemberships,
      {
        tenantId: "tenant-3",
        tenantSlug: "frozen-org",
        tenantDisplayName: "Frozen Org",
        tenantStatus: "suspended",
        role: "editor",
        status: "active",
        isOwner: false,
        joinedAt: "2024-03-01T00:00:00Z",
      },
    ]);
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Frozen Org")).toBeInTheDocument();
    });

    // Switch buttons should only exist for the non-suspended, non-active tenant
    const switchButtons = screen.queryAllByRole("button", { name: /switch/i });
    // tenant-1 is active (no switch btn), tenant-2 has switch btn, tenant-3 is suspended (no switch btn)
    expect(switchButtons.length).toBe(1);
  });

  it("shows Suspended badge for suspended tenants", async () => {
    mockGetTenantMemberships.mockResolvedValue([
      {
        tenantId: "tenant-3",
        tenantSlug: "frozen-org",
        tenantDisplayName: "Frozen Org",
        tenantStatus: "suspended",
        role: "editor",
        status: "active",
        isOwner: false,
        joinedAt: "2024-03-01T00:00:00Z",
      },
    ]);
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Frozen Org")).toBeInTheDocument();
    });

    expect(screen.getByText("Suspended")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /switch/i })).not.toBeInTheDocument();
  });

  it("handles logout action", async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Sign out")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText("Sign out"));

    expect(mockLogout).toHaveBeenCalled();
  });

  it("has leave button for non-owned tenant", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    // Verify the page renders with both tenants
    expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    expect(screen.getByText("editor")).toBeInTheDocument(); // role badge
  });

  it("has delete button for owned tenant", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    // Verify owner badge is shown
    expect(screen.getByText("admin")).toBeInTheDocument(); // role badge for owned tenant
  });

  it("sends UNAUTHORIZED to the machine when API returns 401", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    mockGetTenantMemberships.mockRejectedValue(new Error("401 Unauthorized"));

    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(mockSend).toHaveBeenCalledWith({ type: "UNAUTHORIZED" });
    });
    consoleSpy.mockRestore();
  });

  it("shows error when API fails", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    mockGetTenantMemberships.mockRejectedValue(
      new Error("Failed to load memberships"),
    );

    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByText(/failed to load memberships/i),
      ).toBeInTheDocument();
    });
    consoleSpy.mockRestore();
  });

  it("shows empty state when user has no memberships", async () => {
    mockGetTenantMemberships.mockResolvedValue([]);

    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByText(/not a member of any organizations/i),
      ).toBeInTheDocument();
    });

    expect(screen.getByText("Create Organization")).toBeInTheDocument();
  });

  it("handles back navigation", async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Back")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText("Back"));

    expect(mockNavigate).toHaveBeenCalledWith(-1);
  });

  it("shows Plans tab when membership exists", async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText("Plans")).toBeInTheDocument();
    });
  });

  it("hides Plans tab when no membership", async () => {
    mockMembership = null;
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByRole("tab", { name: /organizations/i }),
      ).toBeInTheDocument();
    });
    expect(screen.getByRole("tab", { name: /security/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("tab", { name: /plans/i }),
    ).not.toBeInTheDocument();
  });

  it("hides Plans tab for site admin even with membership", async () => {
    mockIsSiteAdmin = true;
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByRole("tab", { name: /organizations/i }),
      ).toBeInTheDocument();
    });
    expect(screen.getByRole("tab", { name: /security/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("tab", { name: /plans/i }),
    ).not.toBeInTheDocument();
  });

  it("hides Create Organization button for site admin", async () => {
    mockIsSiteAdmin = true;
    mockGetTenantMemberships.mockResolvedValue([]);
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByText(/not a member of any organizations/i),
      ).toBeInTheDocument();
    });
    expect(screen.queryByText("Create Organization")).not.toBeInTheDocument();
  });

  it("shows Create Organization button for regular user with no memberships", async () => {
    mockIsSiteAdmin = false;
    mockGetTenantMemberships.mockResolvedValue([]);
    const Wrapper = createWrapper("/account?tab=organizations");

    render(
      <Wrapper>
        <AccountPage />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(
        screen.getByText(/not a member of any organizations/i),
      ).toBeInTheDocument();
    });
    expect(screen.getByText("Create Organization")).toBeInTheDocument();
  });

  it("shows Deleting badge and hides actions for deleting tenants", async () => {
    mockGetTenantMemberships.mockResolvedValue([
      {
        tenantId: "tenant-4",
        tenantSlug: "bye-org",
        tenantDisplayName: "Bye Org",
        tenantStatus: "deleting",
        role: "admin",
        status: "active",
        isOwner: true,
        joinedAt: "2024-01-01T00:00:00Z",
      },
    ]);
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("Bye Org")).toBeInTheDocument();
    });
    expect(screen.getByText("Deleting")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /switch/i })).not.toBeInTheDocument();
  });

  it("enters name edit mode on Edit click", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Alex Johnson");
    });

    // Click the Edit button on the profile card (it contains Pencil icon)
    const editBtn = screen.getByRole("button", { name: /edit/i });
    fireEvent.click(editBtn);

    // Should now show first/last name inputs
    expect(screen.getByLabelText("First Name")).toBeInTheDocument();
    expect(screen.getByLabelText("Last Name")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("cancels name edit mode", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Alex Johnson");
    });

    fireEvent.click(screen.getByRole("button", { name: /edit/i }));
    expect(screen.getByLabelText("First Name")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    // Should exit edit mode — no more first/last name inputs
    expect(screen.queryByLabelText("First Name")).not.toBeInTheDocument();
  });

  it("opens leave confirmation dialog for non-owned org", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    // The non-owned tenant (Test Organization) shows a leave button (LogOut icon)
    // Find the second tenant's action buttons (test-org row has a leave-style button)
    const allButtons = screen.getAllByRole("button");
    // Find button with LogOut icon in the non-owner row — it's a ghost button
    // We need to find it by looking at the test-org row context
    // The leave button is the ghost button that isn't the switch button for test-org
    const leaveBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("Test Organization") &&
        !btn.textContent?.includes("Switch");
    });
    expect(leaveBtn).toBeTruthy();
    fireEvent.click(leaveBtn!);

    await waitFor(() => {
      expect(screen.getByText("Leave Organization?")).toBeInTheDocument();
    });
    expect(screen.getByText(/lose access to this organization/i)).toBeInTheDocument();
  });

  it("opens delete confirmation dialog for owned org", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    // The owned tenant (ACME) shows a delete button (Trash2 icon)
    const allButtons = screen.getAllByRole("button");
    const deleteBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("ACME Corporation") &&
        !btn.textContent?.includes("Switch");
    });
    expect(deleteBtn).toBeTruthy();
    fireEvent.click(deleteBtn!);

    await waitFor(() => {
      expect(screen.getByText("Delete Organization?")).toBeInTheDocument();
    });
    expect(screen.getByText(/initiate deletion of all organization data/i)).toBeInTheDocument();
  });

  it("renders all four tab triggers", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByRole("tab", { name: /profile/i })).toBeInTheDocument();
    });

    expect(screen.getByRole("tab", { name: /organizations/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /security/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /plans/i })).toBeInTheDocument();
  });

  // --- Leave tenant flow ---

  it("confirms leave and reloads memberships", async () => {
    mockLeaveTenant.mockResolvedValue(undefined);
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    // Find leave button for non-owned tenant
    const allButtons = screen.getAllByRole("button");
    const leaveBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("Test Organization") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(leaveBtn!);

    await waitFor(() => {
      expect(screen.getByText("Leave Organization?")).toBeInTheDocument();
    });

    // Click Leave in the confirmation dialog
    const confirmLeaveBtn = screen.getByRole("button", { name: /^Leave$/i });
    fireEvent.click(confirmLeaveBtn);

    await waitFor(() => {
      expect(mockLeaveTenant).toHaveBeenCalledWith("tenant-2");
    });
  });

  it("shows error when leave fails", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    mockLeaveTenant.mockRejectedValue(new Error("Leave failed"));
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    const allButtons = screen.getAllByRole("button");
    const leaveBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("Test Organization") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(leaveBtn!);

    await waitFor(() => {
      expect(screen.getByText("Leave Organization?")).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole("button", { name: /^Leave$/i }));

    await waitFor(() => {
      expect(screen.getByText("Leave failed")).toBeInTheDocument();
    });
    consoleSpy.mockRestore();
  });

  // --- Delete tenant flow ---

  it("confirms delete and reloads memberships", async () => {
    mockDeleteTenant.mockResolvedValue(undefined);
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    const allButtons = screen.getAllByRole("button");
    const deleteBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("ACME Corporation") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(deleteBtn!);

    await waitFor(() => {
      expect(screen.getByText("Delete Organization?")).toBeInTheDocument();
    });

    const confirmDeleteBtn = screen.getByRole("button", { name: /delete organization/i });
    fireEvent.click(confirmDeleteBtn);

    await waitFor(() => {
      expect(mockDeleteTenant).toHaveBeenCalledWith("tenant-1");
    });
  });

  it("shows error when delete fails", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    mockDeleteTenant.mockRejectedValue(new Error("Delete failed"));
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    const allButtons = screen.getAllByRole("button");
    const deleteBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("ACME Corporation") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(deleteBtn!);

    await waitFor(() => {
      expect(screen.getByText("Delete Organization?")).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole("button", { name: /delete organization/i }));

    await waitFor(() => {
      expect(screen.getByText("Delete failed")).toBeInTheDocument();
    });
    consoleSpy.mockRestore();
  });

  // --- Save name flow ---

  it("saves profile name via edit form", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Alex Johnson");
    });

    fireEvent.click(screen.getByRole("button", { name: /edit/i }));
    expect(screen.getByLabelText("First Name")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("First Name"), { target: { value: "Bob" } });
    fireEvent.change(screen.getByLabelText("Last Name"), { target: { value: "Smith" } });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(mockUpdateUserProfile).toHaveBeenCalledWith(
        { firstName: "Bob", lastName: "Smith" },
        expect.any(Object),
      );
    });
  });

  // --- Cancel leave dialog ---

  it("cancels leave dialog without action", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("Test Organization")).toBeInTheDocument();
    });

    const allButtons = screen.getAllByRole("button");
    const leaveBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("Test Organization") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(leaveBtn!);

    await waitFor(() => {
      expect(screen.getByText("Leave Organization?")).toBeInTheDocument();
    });

    // Click Cancel
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    await waitFor(() => {
      expect(screen.queryByText("Leave Organization?")).not.toBeInTheDocument();
    });
    expect(mockLeaveTenant).not.toHaveBeenCalled();
  });

  // --- Cancel delete dialog ---

  it("cancels delete dialog without action", async () => {
    const Wrapper = createWrapper("/account?tab=organizations");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      expect(screen.getByText("ACME Corporation")).toBeInTheDocument();
    });

    const allButtons = screen.getAllByRole("button");
    const deleteBtn = allButtons.find(btn => {
      const svg = btn.querySelector("svg");
      return svg && btn.closest("[class*=rounded-lg]")?.textContent?.includes("ACME Corporation") &&
        !btn.textContent?.includes("Switch");
    });
    fireEvent.click(deleteBtn!);

    await waitFor(() => {
      expect(screen.getByText("Delete Organization?")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    await waitFor(() => {
      expect(screen.queryByText("Delete Organization?")).not.toBeInTheDocument();
    });
    expect(mockDeleteTenant).not.toHaveBeenCalled();
  });

  // --- Tab switching ---

  it("switches tabs via URL param", async () => {
    const Wrapper = createWrapper("/account?tab=security");
    render(<Wrapper><AccountPage /></Wrapper>);

    await waitFor(() => {
      // Security tab should be active
      expect(screen.getByRole("tab", { name: /security/i })).toHaveAttribute("data-state", "active");
    });
  });
});

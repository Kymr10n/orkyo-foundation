/* eslint-disable @typescript-eslint/no-explicit-any */
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TenantConfigSettings } from "./TenantConfigSettings";
import type { TenantSettingsResponse } from "@foundation/src/lib/api/tenant-settings-api";

// ── Mocks ───────────────────────────────────────────────────────────

const mockUpdateMutateAsync = vi.fn();
const mockResetMutateAsync = vi.fn();

vi.mock("@foundation/src/hooks/useTenantSettings", () => ({
  useTenantSettings: vi.fn(),
  useUpdateTenantSettings: vi.fn(() => ({
    mutateAsync: mockUpdateMutateAsync,
    isPending: false,
    error: null,
  })),
  useResetTenantSetting: vi.fn(() => ({
    mutateAsync: mockResetMutateAsync,
    isPending: false,
    error: null,
  })),
}));

import {
  useTenantSettings,
  useUpdateTenantSettings,
} from "@foundation/src/hooks/useTenantSettings";

// Auth mock — mutable so tests can override
let mockAuth: any = {
  membership: {
    tenantId: "tenant-1",
    slug: "test",
    displayName: "Test Org",
    isTenantAdmin: true,
    isOwner: false,
    isBreakGlass: false,
  },
};

vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => mockAuth,
}));

// ── Test data ───────────────────────────────────────────────────────

const mockSettings: TenantSettingsResponse = {
  settings: [
    {
      key: "security.password_min_length",
      category: "security",
      displayName: "Minimum Password Length",
      description: "Minimum number of characters required for passwords.",
      valueType: "int",
      defaultValue: "8",
      scope: "tenant",
      minValue: "6",
      maxValue: "128",
      currentValue: "8",
    },
    {
      key: "security.brute_force_lockout_threshold",
      category: "security",
      displayName: "Lockout Threshold",
      description: "Failed login attempts before lockout.",
      valueType: "int",
      defaultValue: "10",
      scope: "site",
      minValue: "3",
      maxValue: "20",
      currentValue: "10",
    },
    {
      key: "invitations.invitation_expiry_days",
      category: "invitations",
      displayName: "Invitation Expiry",
      description: "Days until invitation link expires.",
      valueType: "int",
      defaultValue: "7",
      scope: "tenant",
      minValue: "1",
      maxValue: "90",
      currentValue: "7",
    },
    {
      key: "branding.branding_product_name",
      category: "branding",
      displayName: "Product Name",
      description: "Brand name shown in emails and UI.",
      valueType: "string",
      defaultValue: "Orkyo",
      scope: "tenant",
      minValue: null,
      maxValue: null,
      currentValue: "Orkyo",
    },
    {
      key: "search.search_primary_similarity_threshold",
      category: "search",
      displayName: "Primary Similarity Threshold",
      description: "Minimum similarity score for direct matches.",
      valueType: "double",
      defaultValue: "0.2",
      scope: "tenant",
      minValue: "0.01",
      maxValue: "1",
      currentValue: "0.2",
    },
  ],
};

// Variant with an overridden setting
const mockSettingsWithOverride: TenantSettingsResponse = {
  settings: mockSettings.settings.map((s) =>
    s.key === "branding.branding_product_name"
      ? { ...s, currentValue: "Acme Corp" }
      : s,
  ),
};

// ── Helpers ─────────────────────────────────────────────────────────

function renderComponent() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <TenantConfigSettings />
    </QueryClientProvider>,
  );
}

function setupHook(overrides?: Partial<ReturnType<typeof useTenantSettings>>) {
  vi.mocked(useTenantSettings).mockReturnValue({
    data: mockSettings,
    isLoading: false,
    error: null,
    isError: false,
    isSuccess: true,
    refetch: vi.fn(),
    status: "success",
    ...overrides,
  } as any);
}

// ── Tests ───────────────────────────────────────────────────────────

describe("TenantConfigSettings", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuth = {
      membership: {
        tenantId: "tenant-1",
        slug: "test",
        displayName: "Test Org",
        isTenantAdmin: true,
        isOwner: false,
        isBreakGlass: false,
      },
    };
    mockUpdateMutateAsync.mockResolvedValue(mockSettings);
    mockResetMutateAsync.mockResolvedValue(undefined);
  });

  // ── Access guard ────────────────────────────────────────────────

  it("shows admin-only message for non-admin users", () => {
    mockAuth = {
      membership: {
        tenantId: "tenant-1",
        slug: "test",
        displayName: "Test Org",
        isTenantAdmin: false,
        isOwner: false,
        isBreakGlass: false,
        role: "viewer",
      },
    };
    setupHook();

    renderComponent();

    expect(
      screen.getByText(/only tenant administrators/i),
    ).toBeInTheDocument();
  });

  it("renders for tenant admin", () => {
    setupHook();
    renderComponent();

    expect(
      screen.queryByText(/only tenant administrators/i),
    ).not.toBeInTheDocument();
  });

  it("renders for owner (non-admin)", () => {
    mockAuth = {
      membership: {
        tenantId: "tenant-1",
        slug: "test",
        displayName: "Test Org",
        isTenantAdmin: false,
        isOwner: true,
        isBreakGlass: false,
      },
    };
    setupHook();
    renderComponent();

    expect(
      screen.queryByText(/only tenant administrators/i),
    ).not.toBeInTheDocument();
  });

  it("renders for break-glass user", () => {
    mockAuth = {
      membership: {
        tenantId: "tenant-1",
        slug: "test",
        displayName: "Test Org",
        isTenantAdmin: false,
        isOwner: false,
        isBreakGlass: true,
      },
    };
    setupHook();
    renderComponent();

    expect(
      screen.queryByText(/only tenant administrators/i),
    ).not.toBeInTheDocument();
  });

  // ── Loading / error states ──────────────────────────────────────

  it("shows loading spinner while fetching", () => {
    setupHook({ isLoading: true, data: undefined } as any);
    renderComponent();

    // Spinner is an SVG with animate-spin class — just check no cards render
    expect(screen.queryByText("Security")).not.toBeInTheDocument();
  });

  it("shows error alert on fetch failure", () => {
    setupHook({
      isLoading: false,
      error: new Error("Network error"),
      data: undefined,
    } as any);
    renderComponent();

    expect(
      screen.getByText(/failed to load configuration settings/i),
    ).toBeInTheDocument();
  });

  // ── Category grouping ──────────────────────────────────────────

  it("groups settings by category", () => {
    setupHook();
    renderComponent();

    expect(screen.getByText("Security")).toBeInTheDocument();
    expect(screen.getByText("Invitations")).toBeInTheDocument();
    expect(screen.getByText("Branding")).toBeInTheDocument();
    expect(screen.getByText("Search")).toBeInTheDocument();
  });

  it("shows category descriptions", () => {
    setupHook();
    renderComponent();

    expect(
      screen.getByText(/authentication and access-control settings/i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/customize product name and theme colors/i),
    ).toBeInTheDocument();
  });

  it("renders all setting labels", () => {
    setupHook();
    renderComponent();

    expect(screen.getByText("Minimum Password Length")).toBeInTheDocument();
    expect(screen.getByText("Lockout Threshold")).toBeInTheDocument();
    expect(screen.getByText("Invitation Expiry")).toBeInTheDocument();
    expect(screen.getByText("Product Name")).toBeInTheDocument();
    expect(
      screen.getByText("Primary Similarity Threshold"),
    ).toBeInTheDocument();
  });

  it("shows setting descriptions", () => {
    setupHook();
    renderComponent();

    // Each description appears twice (label area + tooltip) — just check at least one
    expect(
      screen.getAllByText(
        /minimum number of characters required for passwords/i,
      ).length,
    ).toBeGreaterThanOrEqual(1);
  });

  it("shows summary bar with counts", () => {
    setupHook();
    renderComponent();

    expect(screen.getByText(/5 configurable parameters/i)).toBeInTheDocument();
    expect(screen.getByText(/4 categories/i)).toBeInTheDocument();
  });

  // ── Input rendering ────────────────────────────────────────────

  it("renders inputs with current values", () => {
    setupHook();
    renderComponent();

    const pwInput = screen.getByDisplayValue("8");
    expect(pwInput).toBeInTheDocument();

    const brandInput = screen.getByDisplayValue("Orkyo");
    expect(brandInput).toBeInTheDocument();
  });

  // ── Save button state ──────────────────────────────────────────

  it("disables save button when no changes", () => {
    setupHook();
    renderComponent();

    const saveButton = screen.getByRole("button", { name: /save/i });
    expect(saveButton).toBeDisabled();
  });

  it("enables save button after editing", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "12");

    const saveButton = screen.getByRole("button", { name: /save/i });
    expect(saveButton).toBeEnabled();
  });

  it("shows dirty count on save button", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "12");

    expect(screen.getByRole("button", { name: /save \(1\)/i })).toBeInTheDocument();
  });

  // ── Save ───────────────────────────────────────────────────────

  it("calls updateMutation with dirty values on save", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    // Edit password min length
    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "12");

    const saveButton = screen.getByRole("button", { name: /save/i });
    await user.click(saveButton);

    expect(mockUpdateMutateAsync).toHaveBeenCalledWith({
      "security.password_min_length": "12",
    });
  });

  it("sends only changed settings on save", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    // Only edit the product name
    const input = screen.getByDisplayValue("Orkyo");
    await user.clear(input);
    await user.type(input, "Acme");

    const saveButton = screen.getByRole("button", { name: /save/i });
    await user.click(saveButton);

    expect(mockUpdateMutateAsync).toHaveBeenCalledWith({
      "branding.branding_product_name": "Acme",
    });
  });

  // ── Validation ─────────────────────────────────────────────────

  it("shows validation error for non-integer input", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "abc");

    expect(screen.getByText("Must be a whole number")).toBeInTheDocument();
  });

  it("shows validation error when below minimum", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "3");

    expect(screen.getByText("Minimum is 6")).toBeInTheDocument();
  });

  it("shows validation error when above maximum", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "200");

    expect(screen.getByText("Maximum is 128")).toBeInTheDocument();
  });

  it("disables save when validation errors exist", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "abc");

    const saveButton = screen.getByRole("button", { name: /save/i });
    expect(saveButton).toBeDisabled();
  });

  it("validates double fields", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("0.2");
    await user.clear(input);
    await user.type(input, "xyz");

    expect(screen.getByText("Must be a number")).toBeInTheDocument();
  });

  it("validates empty string fields", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("Orkyo");
    await user.clear(input);

    expect(screen.getByText("Cannot be empty")).toBeInTheDocument();
  });

  // ── Modified badge ─────────────────────────────────────────────

  it("shows 'modified' badge for overridden settings", () => {
    setupHook({ data: mockSettingsWithOverride } as any);
    renderComponent();

    expect(screen.getByText("modified")).toBeInTheDocument();
  });

  it("does not show 'modified' badge when all are defaults", () => {
    setupHook();
    renderComponent();

    expect(screen.queryByText("modified")).not.toBeInTheDocument();
  });

  // ── Reset ──────────────────────────────────────────────────────

  it("calls resetMutation when reset button clicked", async () => {
    setupHook({ data: mockSettingsWithOverride } as any);
    renderComponent();
    const user = userEvent.setup();

    // The reset button is only visible for modified settings
    // It's a ghost button with RotateCcw icon — find by role
    const resetButtons = screen.getAllByRole("button").filter(
      (btn) => !btn.textContent?.includes("Save"),
    );

    // Click the first reset button (for the modified branding setting)
    const resetBtn = resetButtons.find((btn) =>
      btn.closest(".flex")?.querySelector(".lucide-rotate-ccw"),
    );

    // Fallback: find the button near the "modified" badge
    if (resetBtn) {
      await user.click(resetBtn);
    } else {
      // Click any non-save button — there should only be one reset button visible
      const allButtons = screen.getAllByRole("button");
      const nonSave = allButtons.filter(
        (b) => !b.textContent?.includes("Save"),
      );
      if (nonSave.length > 0) {
        await user.click(nonSave[0]);
      }
    }

    await waitFor(() => {
      expect(mockResetMutateAsync).toHaveBeenCalledWith(
        "branding.branding_product_name",
      );
    });
  });

  // ── Scope filtering ─────────────────────────────────────────────

  it("filters settings to tenant scope when scope prop is set", () => {
    setupHook({ data: mockSettings } as any);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <TenantConfigSettings scope="tenant" />
      </QueryClientProvider>,
    );

    // The site-scoped brute_force setting should NOT be visible
    expect(
      screen.queryByText("Lockout Threshold"),
    ).not.toBeInTheDocument();

    // Tenant-scoped settings should be visible
    expect(screen.getByText("Minimum Password Length")).toBeInTheDocument();
    expect(screen.getByText("Product Name")).toBeInTheDocument();
  });

  it("filters settings to site scope when scope='site'", () => {
    setupHook({ data: mockSettings } as any);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <TenantConfigSettings scope="site" />
      </QueryClientProvider>,
    );

    // Site-scoped setting should be visible
    expect(screen.getByText("Lockout Threshold")).toBeInTheDocument();

    // Tenant-scoped settings should NOT be visible
    expect(
      screen.queryByText("Minimum Password Length"),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Product Name")).not.toBeInTheDocument();
  });

  it("shows all settings when no scope prop is provided", () => {
    setupHook({ data: mockSettings } as any);
    renderComponent();

    // Both site and tenant scoped settings should appear
    expect(screen.getByText("Lockout Threshold")).toBeInTheDocument();
    expect(screen.getByText("Minimum Password Length")).toBeInTheDocument();
  });

  it("renders with tenantSlug prop for cross-tenant admin", () => {
    setupHook({ data: mockSettings } as any);

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <TenantConfigSettings tenantSlug="acme" scope="tenant" />
      </QueryClientProvider>,
    );

    // Should render tenant-scoped settings (admin guard bypassed via tenantSlug prop)
    expect(screen.getByText("Minimum Password Length")).toBeInTheDocument();
    expect(screen.getByText("Product Name")).toBeInTheDocument();
  });

  it("shows save success feedback after saving", async () => {
    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "12");

    const saveButton = screen.getByRole("button", { name: /save/i });
    await user.click(saveButton);

    await waitFor(() => {
      expect(screen.getByText("Saved")).toBeInTheDocument();
    });
  });

  it("shows error message when save fails", async () => {
    mockUpdateMutateAsync.mockRejectedValueOnce(new Error("Server error"));

    // Need to re-mock useUpdateTenantSettings to expose the error
    vi.mocked(useUpdateTenantSettings).mockReturnValue({
      mutateAsync: mockUpdateMutateAsync,
      isPending: false,
      error: new Error("Server error"),
    } as any);

    setupHook();
    renderComponent();
    const user = userEvent.setup();

    const input = screen.getByDisplayValue("8");
    await user.clear(input);
    await user.type(input, "12");

    // The error message should appear from the mutation error state
    expect(screen.getByText("Server error")).toBeInTheDocument();
  });
});

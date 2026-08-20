/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import type * as ReactRouterDom from "react-router";
import { TourDialog } from "./TourDialog";

// ─── mocks ───────────────────────────────────────────────────────────────────

const mockMarkTourSeen = vi.fn().mockResolvedValue(undefined);
vi.mock("@foundation/src/lib/api/session-api", () => ({
  markTourSeen: () => mockMarkTourSeen(),
}));

// Control auth completion state and edit permission. useCanEdit is mocked directly (mocking useAuth
// underneath it doesn't reach usePermissions through the @foundation self-alias).
const { authState, mockSetAppUser } = vi.hoisted(() => ({
  authState: {
    appUser: { hasSeenTour: false } as { hasSeenTour: boolean } | null,
    canEdit: true,
    isAdmin: true,
  },
  mockSetAppUser: vi.fn(),
}));
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => ({ appUser: authState.appUser, setAppUser: mockSetAppUser }),
}));
vi.mock("@foundation/src/hooks/usePermissions", () => ({
  useCanEdit: () => authState.canEdit,
  useIsTenantAdmin: () => authState.isAdmin,
}));

// `navigateRef` is the indirection that lets a test swap the navigate identity mid-run, which
// is what the router itself does on every pathname change — and what the loop fed on.
const { mockNavigate, navigateRef } = vi.hoisted(() => {
  const fn = vi.fn();
  return { mockNavigate: fn, navigateRef: { current: fn } };
});
vi.mock("react-router", async () => {
  const actual = await vi.importActual<typeof ReactRouterDom>("react-router");
  return { ...actual, useNavigate: () => navigateRef.current };
});

// ─── helpers ──────────────────────────────────────────────────────────────────

function renderTour(props: { open?: boolean; onClose?: () => void } = {}) {
  const onClose = props.onClose ?? vi.fn();
  render(
    <MemoryRouter>
      <TourDialog open={props.open ?? true} onClose={onClose} />
    </MemoryRouter>,
  );
  return { onClose };
}

const next = () => fireEvent.click(screen.getByRole("button", { name: /next/i }));

/** Renders and hands back `rerender`, for the tests that re-render deliberately. */
function renderTourForRerender() {
  return render(
    <MemoryRouter>
      <TourDialog open onClose={vi.fn()} />
    </MemoryRouter>,
  );
}

// Steps an administrator sees: Welcome + 10. An editor loses the admin-only Configuration
// step; a viewer loses that and the two editor-only Settings steps.
const TOTAL_STEPS = 11;
const EDITOR_STEPS = 10;
const VIEWER_STEPS = 8;

// ─── tests ────────────────────────────────────────────────────────────────────

describe("TourDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.appUser = { hasSeenTour: false };
    authState.canEdit = true;
    authState.isAdmin = true;
    navigateRef.current = mockNavigate;
  });

  // ── visibility ───────────────────────────────────────────────────────────────

  it("renders the welcome step when open", () => {
    renderTour();
    expect(screen.getByText("Welcome to Orkyo")).toBeInTheDocument();
    expect(screen.getByText(`1 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("does not render content when closed", () => {
    renderTour({ open: false });
    expect(screen.queryByText("Welcome to Orkyo")).not.toBeInTheDocument();
  });

  // ── navigation through steps ───────────────────────────────────────────────────

  it("Back button is disabled on the first step", () => {
    renderTour();
    expect(screen.getByRole("button", { name: /back/i })).toBeDisabled();
  });

  it("Next advances to the resource-types step", () => {
    renderTour();
    next();
    expect(screen.getByText("Your resource types")).toBeInTheDocument();
    expect(screen.getByText(`2 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("Back returns to the welcome step", () => {
    renderTour();
    next();
    fireEvent.click(screen.getByRole("button", { name: /back/i }));
    expect(screen.getByText("Welcome to Orkyo")).toBeInTheDocument();
  });

  it("dot indicator jumps directly to a step", () => {
    renderTour();
    // [Welcome, Your resource types, Criteria, Templates, ...] → dot index 3 = "Templates"
    fireEvent.click(screen.getAllByRole("button", { name: /go to step/i })[3]);
    expect(screen.getByText("Templates")).toBeInTheDocument();
    expect(screen.getByText(`4 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("shows Done and hides Next on the last step", () => {
    renderTour();
    for (let i = 0; i < TOTAL_STEPS - 1; i++) next();
    expect(screen.getByRole("button", { name: /done/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /next/i })).not.toBeInTheDocument();
  });

  // ── browse: auto-navigation ──────────────────────────────────────────────────

  it("does not navigate when opened on the welcome step", () => {
    renderTour();
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it("navigates the app to each step's page as you advance", () => {
    renderTour();
    next(); // Your resource types
    expect(mockNavigate).toHaveBeenLastCalledWith("/configuration/catalog");
    next(); // Criteria
    expect(mockNavigate).toHaveBeenLastCalledWith("/settings/criteria");
    next(); // Templates
    expect(mockNavigate).toHaveBeenLastCalledWith("/settings/templates");
  });

  it("last step navigates to the Insights tab", () => {
    renderTour();
    for (let i = 0; i < TOTAL_STEPS - 1; i++) next();
    expect(screen.getByText("Insights")).toBeInTheDocument();
    expect(mockNavigate).toHaveBeenLastCalledWith("/insights/overview");
  });

  it("navigates once per step, however often the router re-renders it", () => {
    // The regression this pins. Under BrowserRouter `navigate` takes a new identity on every
    // pathname change, and a step can point at a route that redirects on arrival (/stations
    // sends you to the first station type's list). Re-running the effect on that new identity
    // pushed the redirecting route again — an endless loop showing the blank half of the
    // redirect. Here a fresh `navigate` identity stands in for the redirect having happened.
    const { rerender } = renderTourForRerender();

    next(); // Your resource types
    expect(mockNavigate).toHaveBeenCalledTimes(1);

    mockNavigate.mockClear();
    const afterRedirect = vi.fn();
    navigateRef.current = afterRedirect;
    rerender(
      <MemoryRouter>
        <TourDialog open onClose={vi.fn()} />
      </MemoryRouter>,
    );

    expect(afterRedirect).not.toHaveBeenCalled();

    next(); // Criteria — a new step does navigate
    expect(afterRedirect).toHaveBeenCalledTimes(1);
    expect(afterRedirect).toHaveBeenCalledWith("/settings/criteria");
  });

  it("navigates again when the tour is reopened", () => {
    // The guard remembers the step it navigated for; reopening has to forget it, or the tour
    // reopens on a page the user has since navigated away from.
    const { rerender } = renderTourForRerender();
    next();
    expect(mockNavigate).toHaveBeenCalledTimes(1);

    rerender(
      <MemoryRouter>
        <TourDialog open={false} onClose={vi.fn()} />
      </MemoryRouter>,
    );
    mockNavigate.mockClear();
    rerender(
      <MemoryRouter>
        <TourDialog open onClose={vi.fn()} />
      </MemoryRouter>,
    );

    // Reopening restarts at Welcome, which has no path; the first Next navigates again.
    next();
    expect(mockNavigate).toHaveBeenCalledWith("/configuration/catalog");
  });

  // ── permission-aware steps ───────────────────────────────────────────────────

  it("hides editor-only steps for viewers and never navigates to Settings", () => {
    authState.canEdit = false;
    authState.isAdmin = false;
    renderTour();

    expect(screen.getByText(`1 / ${VIEWER_STEPS}`)).toBeInTheDocument();
    next(); // first step after Welcome is Stations, not Criteria
    expect(screen.getByText("Stations")).toBeInTheDocument();
    expect(screen.queryByText("Criteria")).not.toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalledWith("/settings/criteria");
  });

  it("hides the admin-only Configuration step from an editor", () => {
    // Configuration is tenant-admin only, so an editor walked to it would be bounced.
    authState.isAdmin = false;
    renderTour();

    expect(screen.getByText(`1 / ${EDITOR_STEPS}`)).toBeInTheDocument();
    next();
    expect(screen.getByText("Criteria")).toBeInTheDocument();
    expect(screen.queryByText("Your resource types")).not.toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalledWith("/configuration/catalog");
  });

  // ── close / markTourSeen ─────────────────────────────────────────────────────

  it("clicking Done calls markTourSeen and onClose", async () => {
    const { onClose } = renderTour();
    for (let i = 0; i < TOTAL_STEPS - 1; i++) next();
    fireEvent.click(screen.getByRole("button", { name: /done/i }));
    await waitFor(() => {
      expect(mockMarkTourSeen).toHaveBeenCalledTimes(1);
      expect(onClose).toHaveBeenCalledTimes(1);
    });
  });

  it("closing reflects completion in local auth state (survives remounts)", async () => {
    renderTour();
    fireEvent.click(screen.getByRole("button", { name: /close tour/i }));
    await waitFor(() =>
      expect(mockSetAppUser).toHaveBeenCalledWith(expect.objectContaining({ hasSeenTour: true })),
    );
  });

  it("does not re-update auth state when the tour was already seen", async () => {
    authState.appUser = { hasSeenTour: true };
    renderTour();
    fireEvent.click(screen.getByRole("button", { name: /close tour/i }));
    await waitFor(() => expect(mockMarkTourSeen).toHaveBeenCalled());
    expect(mockSetAppUser).not.toHaveBeenCalled();
  });

  it("markTourSeen failure is non-fatal (still closes)", async () => {
    mockMarkTourSeen.mockRejectedValueOnce(new Error("Network error"));
    const { onClose } = renderTour();
    fireEvent.click(screen.getByRole("button", { name: /close tour/i }));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });
});

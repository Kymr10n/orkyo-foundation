/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import type * as ReactRouterDom from "react-router-dom";
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
  },
  mockSetAppUser: vi.fn(),
}));
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => ({ appUser: authState.appUser, setAppUser: mockSetAppUser }),
}));
vi.mock("@foundation/src/hooks/usePermissions", () => ({
  useCanEdit: () => authState.canEdit,
}));

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof ReactRouterDom>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
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

// Steps as an editor sees them (Welcome + 9). Viewers drop the two editor-only steps.
const TOTAL_STEPS = 10;

// ─── tests ────────────────────────────────────────────────────────────────────

describe("TourDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.appUser = { hasSeenTour: false };
    authState.canEdit = true;
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

  it("Next advances to the Criteria step", () => {
    renderTour();
    next();
    expect(screen.getByText("Criteria")).toBeInTheDocument();
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
    // [Welcome, Criteria, Templates, Groups, ...] → dot index 3 = "Groups"
    fireEvent.click(screen.getAllByRole("button", { name: /go to step/i })[3]);
    expect(screen.getByText("Groups")).toBeInTheDocument();
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
    next(); // Criteria
    expect(mockNavigate).toHaveBeenLastCalledWith("/settings/criteria");
    next(); // Templates
    expect(mockNavigate).toHaveBeenLastCalledWith("/settings/templates");
  });

  it("last step navigates to the Insights tab", () => {
    renderTour();
    for (let i = 0; i < TOTAL_STEPS - 1; i++) next();
    expect(screen.getByText("Insights")).toBeInTheDocument();
    expect(mockNavigate).toHaveBeenLastCalledWith("/?tab=insights");
  });

  // ── permission-aware steps ───────────────────────────────────────────────────

  it("hides editor-only steps (Criteria/Templates) for viewers and never navigates to Settings", () => {
    authState.canEdit = false;
    renderTour();

    expect(screen.getByText(`1 / 8`)).toBeInTheDocument(); // Welcome + 7 viewer-visible steps
    next(); // first step after Welcome is Groups, not Criteria
    expect(screen.getByText("Groups")).toBeInTheDocument();
    expect(screen.queryByText("Criteria")).not.toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalledWith("/settings/criteria");
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

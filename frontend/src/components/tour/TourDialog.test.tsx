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

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual =
    await vi.importActual<typeof ReactRouterDom>("react-router-dom");
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
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

// Total number of STEPS defined in TourDialog
const TOTAL_STEPS = 7;

// ─── tests ────────────────────────────────────────────────────────────────────

describe("TourDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── visibility ───────────────────────────────────────────────────────────────

  it("renders first step content when open", () => {
    renderTour();

    expect(screen.getByText("Criteria")).toBeInTheDocument();
    expect(screen.getByText(`1 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("does not render content when closed", () => {
    renderTour({ open: false });

    expect(screen.queryByText("Criteria")).not.toBeInTheDocument();
  });

  // ── navigation ───────────────────────────────────────────────────────────────

  it("Back button is disabled on the first step", () => {
    renderTour();

    const back = screen.getByRole("button", { name: /back/i });
    expect(back).toBeDisabled();
  });

  it("Next button advances to step 2", () => {
    renderTour();

    fireEvent.click(screen.getByRole("button", { name: /next/i }));

    expect(screen.getByText("Space Templates")).toBeInTheDocument();
    expect(screen.getByText(`2 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("Back button returns from step 2 to step 1", () => {
    renderTour();

    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    fireEvent.click(screen.getByRole("button", { name: /back/i }));

    expect(screen.getByText("Criteria")).toBeInTheDocument();
    expect(screen.getByText(`1 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it("dot indicator navigates directly to the clicked step", () => {
    renderTour();

    // Dots are labeled "Go to step N" — click dot 4 for "Spaces" (index 3)
    const dots = screen.getAllByRole("button", { name: /go to step/i });
    fireEvent.click(dots[3]);

    expect(screen.getByText("Spaces")).toBeInTheDocument();
    expect(screen.getByText(`4 / ${TOTAL_STEPS}`)).toBeInTheDocument();
  });

  it(`shows Done button and hides Next on the last step (step ${TOTAL_STEPS})`, () => {
    renderTour();

    for (let i = 0; i < TOTAL_STEPS - 1; i++) {
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
    }

    expect(screen.getByRole("button", { name: /done/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /next/i })).not.toBeInTheDocument();
  });

  // ── close / markTourSeen ─────────────────────────────────────────────────────

  it("clicking Done calls markTourSeen and onClose", async () => {
    const { onClose } = renderTour();

    // Navigate to last step
    for (let i = 0; i < TOTAL_STEPS - 1; i++) {
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
    }

    fireEvent.click(screen.getByRole("button", { name: /done/i }));

    await waitFor(() => {
      expect(mockMarkTourSeen).toHaveBeenCalledTimes(1);
      expect(onClose).toHaveBeenCalledTimes(1);
    });
  });

  it("markTourSeen is called even if it throws (non-fatal)", async () => {
    mockMarkTourSeen.mockRejectedValueOnce(new Error("Network error"));
    const { onClose } = renderTour();

    for (let i = 0; i < TOTAL_STEPS - 1; i++) {
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
    }

    fireEvent.click(screen.getByRole("button", { name: /done/i }));

    // onClose still called despite markTourSeen throwing
    await waitFor(() => {
      expect(onClose).toHaveBeenCalledTimes(1);
    });
  });

  // ── action buttons ───────────────────────────────────────────────────────────

  it("action button on step 1 navigates to the correct path", async () => {
    const { onClose } = renderTour();

    fireEvent.click(screen.getByRole("button", { name: /go to criteria/i }));

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith("/settings?tab=criteria");
      expect(onClose).toHaveBeenCalledTimes(1);
    });
  });

  it("each step that has an action shows the action button", () => {
    renderTour();

    // Step 1 "Criteria" has action "Go to Criteria"
    expect(
      screen.getByRole("button", { name: /go to criteria/i }),
    ).toBeInTheDocument();

    // Step 7 "Utilization" should also have action "Go to Utilization"
    for (let i = 0; i < TOTAL_STEPS - 1; i++) {
      fireEvent.click(screen.getByRole("button", { name: /next/i }));
    }
    expect(
      screen.getByRole("button", { name: /go to utilization/i }),
    ).toBeInTheDocument();
  });
});

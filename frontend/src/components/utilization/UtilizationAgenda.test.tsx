import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";
import { UtilizationAgenda } from "./UtilizationAgenda";
import { makeRequest } from "@foundation/src/test-utils/request-fixtures";
import type { Conflict } from "@foundation/src/types/requests";

function makeConflict(severity: Conflict["severity"]): Conflict {
  return { id: `c-${severity}`, kind: "overlap", severity, message: "clash" };
}

const early = makeRequest({
  id: "r-early",
  name: "Morning install",
  startTs: "2026-06-15T08:00:00Z",
  endTs: "2026-06-15T10:00:00Z",
  status: "new",
});
const late = makeRequest({
  id: "r-late",
  name: "Afternoon service",
  startTs: "2026-06-15T14:00:00Z",
  endTs: "2026-06-15T16:00:00Z",
  status: "new",
});
const unscheduled = makeRequest({
  id: "r-none",
  name: "Backlog item",
  startTs: null,
  endTs: null,
});

describe("UtilizationAgenda", () => {
  it("lists scheduled requests as tappable items", () => {
    render(<UtilizationAgenda requests={[early, late]} onOpen={vi.fn()} />);
    expect(screen.getByText("Morning install")).toBeInTheDocument();
    expect(screen.getByText("Afternoon service")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
  });

  it("orders items chronologically by start time", () => {
    render(<UtilizationAgenda requests={[late, early]} onOpen={vi.fn()} />);
    const items = screen.getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Morning install");
    expect(items[1]).toHaveTextContent("Afternoon service");
  });

  it("omits unscheduled requests (no start time)", () => {
    render(<UtilizationAgenda requests={[early, unscheduled]} onOpen={vi.fn()} />);
    expect(screen.getByText("Morning install")).toBeInTheDocument();
    expect(screen.queryByText("Backlog item")).not.toBeInTheDocument();
  });

  it("shows the empty message when nothing is scheduled", () => {
    render(<UtilizationAgenda requests={[unscheduled]} onOpen={vi.fn()} emptyMessage="Nothing today." />);
    expect(screen.getByText("Nothing today.")).toBeInTheDocument();
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
  });

  it("calls onOpen with the request when an item is tapped", () => {
    const onOpen = vi.fn();
    render(<UtilizationAgenda requests={[early]} onOpen={onOpen} />);
    fireEvent.click(screen.getByText("Morning install"));
    expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ id: "r-early" }));
  });

  it("shows no conflict icon when the conflicts prop is omitted", () => {
    render(<UtilizationAgenda requests={[early, late]} onOpen={vi.fn()} />);
    expect(screen.queryByTestId("agenda-conflict-icon")).not.toBeInTheDocument();
  });

  it("flags only the conflicted card with a severity icon", () => {
    const conflicts = new Map([["r-early", [makeConflict("warning")]]]);
    render(<UtilizationAgenda requests={[early, late]} onOpen={vi.fn()} conflicts={conflicts} />);
    const items = screen.getAllByRole("listitem");
    expect(within(items[0]).getByTestId("agenda-conflict-icon")).toHaveAttribute("title", "Warning");
    expect(within(items[1]).queryByTestId("agenda-conflict-icon")).not.toBeInTheDocument();
  });

  it("lets error severity dominate warning", () => {
    const conflicts = new Map([["r-early", [makeConflict("warning"), makeConflict("error")]]]);
    render(<UtilizationAgenda requests={[early]} onOpen={vi.fn()} conflicts={conflicts} />);
    expect(screen.getByTestId("agenda-conflict-icon")).toHaveAttribute("title", "Error");
  });

  it("shows no icon for an empty conflict list", () => {
    const conflicts = new Map<string, Conflict[]>([["r-early", []]]);
    render(<UtilizationAgenda requests={[early]} onOpen={vi.fn()} conflicts={conflicts} />);
    expect(screen.queryByTestId("agenda-conflict-icon")).not.toBeInTheDocument();
  });
});

import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { UtilizationAgenda } from "./UtilizationAgenda";
import { makeRequest } from "@foundation/src/test-utils/request-fixtures";

const early = makeRequest({
  id: "r-early",
  name: "Morning install",
  startTs: "2026-06-15T08:00:00Z",
  endTs: "2026-06-15T10:00:00Z",
  status: "planned",
});
const late = makeRequest({
  id: "r-late",
  name: "Afternoon service",
  startTs: "2026-06-15T14:00:00Z",
  endTs: "2026-06-15T16:00:00Z",
  status: "planned",
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
});

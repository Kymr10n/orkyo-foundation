import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { InsightsFilters, resolveRange } from "./InsightsFilters";

describe("InsightsFilters", () => {
  beforeEach(() => vi.clearAllMocks());

  const props = {
    range: "window" as const,
    onRangeChange: vi.fn(),
    bucket: "month" as const,
    onBucketChange: vi.fn(),
  };

  it("renders the current range and bucket", () => {
    render(<InsightsFilters {...props} />);
    expect(screen.getByText("Last 6 / next 12 mo")).toBeInTheDocument();
    expect(screen.getByText("Monthly")).toBeInTheDocument();
  });

  it("calls onBucketChange when a different bucket is selected", () => {
    render(<InsightsFilters {...props} />);
    fireEvent.click(screen.getByRole("combobox", { name: "Bucket" }));
    fireEvent.click(screen.getByText("Weekly"));
    expect(props.onBucketChange).toHaveBeenCalledWith("week");
  });

  it("calls onRangeChange when a different range is selected", () => {
    render(<InsightsFilters {...props} />);
    fireEvent.click(screen.getByRole("combobox", { name: "Date range" }));
    fireEvent.click(screen.getByText("Next 12 months"));
    expect(props.onRangeChange).toHaveBeenCalledWith("next12m");
  });
});

describe("resolveRange", () => {
  it("extends forward-looking presets past now", () => {
    const next12 = resolveRange("next12m");
    expect(next12.to.getTime()).toBeGreaterThan(Date.now());
    expect(next12.from.getTime()).toBeLessThanOrEqual(Date.now() + 1000);
  });

  it("centers the default window around now (history + planning horizon)", () => {
    const { from, to } = resolveRange("window");
    expect(from.getTime()).toBeLessThan(Date.now()); // ~6 months of history
    expect(to.getTime()).toBeGreaterThan(Date.now()); // ~12 months ahead
  });
});

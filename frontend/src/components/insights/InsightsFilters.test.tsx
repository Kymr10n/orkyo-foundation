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

  it("floors the window to the start of the day so the cache key stays stable across loads", () => {
    const { from, to } = resolveRange("window");
    // No sub-day precision — otherwise the millisecond-anchored window produced a unique cache
    // key on every load (0% insights-cache hits).
    for (const d of [from, to]) {
      expect(d.getHours()).toBe(0);
      expect(d.getMinutes()).toBe(0);
      expect(d.getSeconds()).toBe(0);
      expect(d.getMilliseconds()).toBe(0);
    }
    // Two calls within the same day are byte-identical → React-Query and server cache keys repeat.
    const again = resolveRange("window");
    expect(again.from.getTime()).toBe(from.getTime());
    expect(again.to.getTime()).toBe(to.getTime());
  });
});

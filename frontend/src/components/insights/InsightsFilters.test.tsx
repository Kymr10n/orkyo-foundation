import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { InsightsFilters, resolveRange } from "./InsightsFilters";

describe("InsightsFilters", () => {
  beforeEach(() => vi.clearAllMocks());

  const props = {
    range: "12m" as const,
    onRangeChange: vi.fn(),
    bucket: "month" as const,
    onBucketChange: vi.fn(),
  };

  it("renders the current range and bucket", () => {
    render(<InsightsFilters {...props} />);
    expect(screen.getByText("Last 12 months")).toBeInTheDocument();
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
    fireEvent.click(screen.getByText("Last 30 days"));
    expect(props.onRangeChange).toHaveBeenCalledWith("30d");
  });
});

describe("resolveRange", () => {
  it("produces distinct, ordered windows per preset", () => {
    const thirty = resolveRange("30d");
    const year = resolveRange("12m");

    expect(thirty.from.getTime()).toBeLessThan(thirty.to.getTime());
    // A 30-day window starts later (nearer to now) than a 12-month window.
    expect(thirty.from.getTime()).toBeGreaterThan(year.from.getTime());
  });

  it("anchors year-to-date at January 1st", () => {
    const { from } = resolveRange("ytd");
    expect(from.getMonth()).toBe(0);
    expect(from.getDate()).toBe(1);
  });
});

import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  ConflictTrendChart,
  RequestStatusTrendChart,
  UtilizationTrendChart,
} from "./InsightsTrendCharts";
import type {
  InsightsBucket,
  InsightsConflicts,
  InsightsRequests,
  InsightsUtilization,
} from "@foundation/src/lib/api/insights-api";

// Recharts needs a real layout box (absent in happy-dom) — stub to passthroughs.
vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: React.ReactNode }) => <div>{children}</div>;
  const Noop = () => null;
  return {
    ResponsiveContainer: Pass, LineChart: Pass, BarChart: Pass,
    Line: Noop, Bar: Noop, XAxis: Noop, YAxis: Noop, Tooltip: Noop, Legend: Noop, CartesianGrid: Noop,
  };
});

const meta = { calculatedAt: "2026-06-22T10:00:00Z", sourceMode: "live" };

const util = (bucket: InsightsBucket): InsightsUtilization => ({
  resourceType: "space",
  bucket,
  series: [
    { bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", totalCapacityMinutes: 1000, usedCapacityMinutes: 500, availableCapacityMinutes: 500, utilizationPercent: 50, conflictCount: 1 },
    // null %, but capacity > 0 → not "empty"; exercises the null branch of the display clamp.
    { bucketStart: "2026-02-01T00:00:00Z", bucketEnd: "2026-03-01T00:00:00Z", totalCapacityMinutes: 1000, usedCapacityMinutes: 0, availableCapacityMinutes: 1000, utilizationPercent: null, conflictCount: 0 },
    // overbooked > 100% → display clamps to 100%.
    { bucketStart: "2026-04-01T00:00:00Z", bucketEnd: "2026-05-01T00:00:00Z", totalCapacityMinutes: 1000, usedCapacityMinutes: 1500, availableCapacityMinutes: 0, utilizationPercent: 150, conflictCount: 0 },
  ],
  metadata: meta,
});

describe("InsightsTrendCharts", () => {
  it("renders the utilization line chart with data (no empty state)", () => {
    render(<UtilizationTrendChart title="Space utilization trend" data={util("month")} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("Space utilization trend")).toBeInTheDocument();
    expect(screen.queryByText(/No capacity configured/)).not.toBeInTheDocument();
  });

  it.each(["week", "quarter", "year"] as InsightsBucket[])(
    "formats bucket labels for the %s granularity",
    (bucket) => {
      // Exercises the bucketLabel branch for each granularity without throwing.
      const { container } = render(
        <UtilizationTrendChart title="t" data={util(bucket)} bucket={bucket} isLoading={false} error={null} />,
      );
      expect(container).toBeTruthy();
    },
  );

  it("shows the loading state", () => {
    render(<UtilizationTrendChart title="t" data={undefined} bucket="month" isLoading={true} error={null} />);
    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("shows the error state", () => {
    render(<UtilizationTrendChart title="t" data={undefined} bucket="month" isLoading={false} error={new Error("x")} />);
    expect(screen.getByText(/Could not load this chart/)).toBeInTheDocument();
  });

  it("renders the conflict stacked bars with data", () => {
    const data: InsightsConflicts = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 5, overbooking: 2, criteriaMismatch: 1, resourceUnavailable: 1, scheduleOutsideAvailability: 1, missingResource: 0 }],
      metadata: meta,
    };
    render(<ConflictTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("Conflict trend")).toBeInTheDocument();
    expect(screen.queryByText(/No conflicts/)).not.toBeInTheDocument();
  });

  it("shows the conflict empty state when all buckets are zero", () => {
    const data: InsightsConflicts = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 0, overbooking: 0, criteriaMismatch: 0, resourceUnavailable: 0, scheduleOutsideAvailability: 0, missingResource: 0 }],
      metadata: meta,
    };
    render(<ConflictTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("No conflicts in this period.")).toBeInTheDocument();
  });

  it("renders the request status stacked bars with data", () => {
    const data: InsightsRequests = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 10, planned: 4, inProgress: 2, done: 3, cancelled: 1 }],
      metadata: meta,
    };
    render(<RequestStatusTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("Request status trend")).toBeInTheDocument();
    expect(screen.queryByText(/No scheduled requests/)).not.toBeInTheDocument();
  });

  it("shows the request empty state when all buckets are zero", () => {
    const data: InsightsRequests = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 0, planned: 0, inProgress: 0, done: 0, cancelled: 0 }],
      metadata: meta,
    };
    render(<RequestStatusTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("No scheduled requests in this period.")).toBeInTheDocument();
  });
});

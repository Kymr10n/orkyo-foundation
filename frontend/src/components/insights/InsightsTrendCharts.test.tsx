import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  BottleneckChart,
  ConflictTrendChart,
  RequestStatusTrendChart,
  UtilizationTrendChart,
} from "./InsightsTrendCharts";
import type {
  InsightsBottlenecks,
  InsightsBucket,
  InsightsConflicts,
  InsightsRequests,
  InsightsUtilization,
} from "@foundation/src/lib/api/insights-api";

// Recharts needs a real layout box (absent in happy-dom) — stub to passthroughs. Tooltip and
// YAxis record their props instead of rendering: their formatters are real display logic that
// a Noop stub would leave permanently unexecuted.
const tooltipProps = vi.hoisted(() => [] as Record<string, unknown>[]);
const yAxisProps = vi.hoisted(() => [] as Record<string, unknown>[]);
vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: React.ReactNode }) => <div>{children}</div>;
  const Noop = () => null;
  return {
    ResponsiveContainer: Pass, LineChart: Pass, BarChart: Pass,
    Line: Noop, Bar: Noop, XAxis: Noop, Legend: Noop, CartesianGrid: Noop,
    YAxis: (props: Record<string, unknown>) => { yAxisProps.push(props); return null; },
    Tooltip: (props: Record<string, unknown>) => { tooltipProps.push(props); return null; },
  };
});

const meta = { calculatedAt: "2026-06-22T10:00:00Z", sourceMode: "live" };

const util = (bucket: InsightsBucket): InsightsUtilization => ({
  resourceType: "space",
  resourceCount: 2,
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

const bottlenecks = (
  overrides: Partial<InsightsBottlenecks["items"][number]> = {},
): InsightsBottlenecks => ({
  items: [
    {
      resourceId: "res-1",
      name: "Line 1",
      resourceTypeKey: "machine",
      resourceTypeDisplayName: "Machine",
      overbookedMinutes: 150,
      peakUtilizationPercent: 142.4,
      ...overrides,
    },
  ],
  metadata: meta,
} as unknown as InsightsBottlenecks);

beforeEach(() => {
  tooltipProps.length = 0;
  yAxisProps.length = 0;
});

describe("BottleneckChart", () => {
  it("says plainly that nothing was overbooked rather than showing a blank chart", () => {
    render(<BottleneckChart data={{ items: [], metadata: meta } as unknown as InsightsBottlenecks} isLoading={false} error={null} />);
    expect(screen.getByText(/No resource was booked beyond its capacity/)).toBeInTheDocument();
  });

  it("quotes hours over capacity and the peak in the tooltip", () => {
    render(<BottleneckChart data={bottlenecks()} isLoading={false} error={null} />);
    const { formatter, labelFormatter } = tooltipProps[0] as {
      formatter: (v: unknown, n: unknown, e: unknown) => [string, string];
      labelFormatter: (l: unknown, p: unknown) => string;
    };
    const payload = [{ payload: { id: "res-1", name: "Line 1", type: "Machine", peak: 142 } }];

    expect(formatter(2.5, "hours", payload[0])).toEqual(["2.5 h over capacity (peak 142%)", "Overbooked"]);
    expect(labelFormatter("ignored", payload)).toBe("Line 1 — Machine");
  });

  it("drops the peak clause when the resource published no capacity", () => {
    // 0% would read as "not busy" for a resource that is, in fact, overbooked.
    render(<BottleneckChart data={bottlenecks({ peakUtilizationPercent: null })} isLoading={false} error={null} />);
    const { formatter } = tooltipProps[0] as { formatter: (v: unknown, n: unknown, e: unknown) => [string, string] };

    expect(formatter(2.5, "hours", { payload: { peak: null } })[0]).toBe("2.5 h over capacity");
  });

  it("takes a per-type title, and keeps the generic one when none is given", () => {
    const { unmount } = render(<BottleneckChart data={bottlenecks()} isLoading={false} error={null} />);
    expect(screen.getByText("Most overloaded resources")).toBeInTheDocument();
    unmount();

    render(<BottleneckChart title="Most overloaded machines" data={bottlenecks()} isLoading={false} error={null} />);
    expect(screen.getByText("Most overloaded machines")).toBeInTheDocument();
  });

  it("labels the axis by resource id so two resources sharing a name stay separate bars", () => {
    render(<BottleneckChart data={bottlenecks()} isLoading={false} error={null} />);
    const axis = yAxisProps[0] as { dataKey: string; tickFormatter: (id: string) => string };

    expect(axis.dataKey).toBe("id");
    expect(axis.tickFormatter("res-1")).toBe("Line 1");
    // An id the chart does not know must not render as "undefined".
    expect(axis.tickFormatter("gone")).toBe("");
  });
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
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 5, overbooking: 2, criteriaMismatch: 1, resourceUnavailable: 1, scheduleOutsideAvailability: 1, missingResource: 0, sequenceViolation: 0 }],
      metadata: meta,
    };
    render(<ConflictTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("Conflict trend")).toBeInTheDocument();
    expect(screen.queryByText(/No conflicts/)).not.toBeInTheDocument();
  });

  it("shows the conflict empty state when all buckets are zero", () => {
    const data: InsightsConflicts = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 0, overbooking: 0, criteriaMismatch: 0, resourceUnavailable: 0, scheduleOutsideAvailability: 0, missingResource: 0, sequenceViolation: 0 }],
      metadata: meta,
    };
    render(<ConflictTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("No conflicts in this period.")).toBeInTheDocument();
  });

  it("renders the request status stacked bars with data", () => {
    const data: InsightsRequests = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 10, new: 4, inProgress: 2, done: 3, deferred: 0, cancelled: 1 }],
      metadata: meta,
    };
    render(<RequestStatusTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("Request status trend")).toBeInTheDocument();
    expect(screen.queryByText(/No scheduled requests/)).not.toBeInTheDocument();
  });

  it("shows the request empty state when all buckets are zero", () => {
    const data: InsightsRequests = {
      bucket: "month",
      series: [{ bucketStart: "2026-01-01T00:00:00Z", bucketEnd: "2026-02-01T00:00:00Z", total: 0, new: 0, inProgress: 0, done: 0, deferred: 0, cancelled: 0 }],
      metadata: meta,
    };
    render(<RequestStatusTrendChart data={data} bucket="month" isLoading={false} error={null} />);
    expect(screen.getByText("No scheduled requests in this period.")).toBeInTheDocument();
  });
});

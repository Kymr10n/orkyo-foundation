import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen } from "@testing-library/react";
import { UtilizationTab } from "./UtilizationTab";
import { useInsightsUtilization } from "@foundation/src/hooks/useInsights";

// Window comes from the router <Outlet context> — pin it.
vi.mock("@foundation/src/components/insights/insightsTabContext", () => ({
  useInsightsTabContext: () => ({ from: new Date("2026-01-01"), to: new Date("2026-12-31"), bucket: "month", siteId: null }),
}));

vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: React.ReactNode }) => <div>{children}</div>;
  const Noop = () => null;
  return {
    ResponsiveContainer: Pass, LineChart: Pass, BarChart: Pass,
    Line: Noop, Bar: Noop, XAxis: Noop, YAxis: Noop, Tooltip: Noop, Legend: Noop, CartesianGrid: Noop,
  };
});

vi.mock("@foundation/src/hooks/useInsights", () => ({
  useInsightsUtilization: vi.fn(),
}));

// The chart list is driven by the tenant's resource types, so it needs a QueryClient it never
// had before; mocked here in the same style as the insights hooks above.
vi.mock("@foundation/src/hooks/useResourceTypes", () => ({
  useResourceTypes: () => ({
    data: [
      { id: "rt-space", key: "space", displayName: "Space", isSystem: true, isActive: true },
      { id: "rt-person", key: "person", displayName: "Person", isSystem: true, isActive: true },
      { id: "rt-vehicle", key: "vehicle", displayName: "Vehicle", isSystem: false, isActive: true },
    ],
  }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("UtilizationTab", () => {
  it("renders one chart per active resource type, including tenant-defined ones", () => {
    (useInsightsUtilization as Mock).mockReturnValue({
      data: { resourceType: "space", bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });

    render(<UtilizationTab />);

    expect(screen.getByText("Space utilization trend")).toBeInTheDocument();
    expect(screen.getByText("Person utilization trend")).toBeInTheDocument();
    // Previously impossible: the chart list was two hard-coded hook calls.
    expect(screen.getByText("Vehicle utilization trend")).toBeInTheDocument();
    expect(screen.getAllByText("No capacity configured for this period.")).toHaveLength(3);
  });
});

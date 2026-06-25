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

beforeEach(() => {
  vi.clearAllMocks();
});

describe("UtilizationTab", () => {
  it("renders both space and people utilization charts (empty states when no capacity)", () => {
    (useInsightsUtilization as Mock).mockReturnValue({
      data: { resourceType: "space", bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });

    render(<UtilizationTab />);

    expect(screen.getByText("Space utilization trend")).toBeInTheDocument();
    expect(screen.getByText("People utilization trend")).toBeInTheDocument();
    expect(screen.getAllByText("No capacity configured for this period.")).toHaveLength(2);
  });
});

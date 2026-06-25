import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen } from "@testing-library/react";
import { OverviewTab } from "./OverviewTab";
import { useInsightsOverview, useInsightsRequests } from "@foundation/src/hooks/useInsights";
import type { InsightsOverview } from "@foundation/src/lib/api/insights-api";

// Window comes from the router <Outlet context> — pin it.
vi.mock("@foundation/src/components/insights/insightsTabContext", () => ({
  useInsightsTabContext: () => ({ from: new Date("2026-01-01"), to: new Date("2026-12-31"), bucket: "month", siteId: null }),
}));

// Recharts needs a real layout box (absent in happy-dom); stub to passthroughs.
vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: React.ReactNode }) => <div>{children}</div>;
  const Noop = () => null;
  return {
    ResponsiveContainer: Pass, LineChart: Pass, BarChart: Pass,
    Line: Noop, Bar: Noop, XAxis: Noop, YAxis: Noop, Tooltip: Noop, Legend: Noop, CartesianGrid: Noop,
  };
});

vi.mock("@foundation/src/hooks/useInsights", () => ({
  useInsightsOverview: vi.fn(),
  useInsightsRequests: vi.fn(),
}));

const idle = { data: undefined, isLoading: false, error: null };

function overviewData(overrides?: Partial<InsightsOverview>): InsightsOverview {
  return {
    period: { from: "2026-01-01T00:00:00Z", to: "2026-12-31T00:00:00Z" },
    siteId: null,
    requests: { total: 120, scheduled: 80, unscheduled: 30, completed: 50, cancelled: 10 },
    conflicts: { total: 7, overbooking: 3, criteriaMismatch: 2, resourceUnavailable: 2, scheduleOutsideAvailability: 0, missingResource: 0 },
    utilization: { spacesPercent: 74.2, peoplePercent: null, toolsPercent: null },
    metadata: { calculatedAt: "2026-06-22T10:00:00Z", sourceMode: "live" },
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  (useInsightsOverview as Mock).mockReturnValue(idle);
  (useInsightsRequests as Mock).mockReturnValue(idle);
});

describe("OverviewTab", () => {
  it("renders KPI cards from overview data", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: overviewData(), isLoading: false, error: null });

    render(<OverviewTab />);

    expect(screen.getByText("Total requests")).toBeInTheDocument();
    expect(screen.getByText("120")).toBeInTheDocument();   // total
    expect(screen.getByText("74.2%")).toBeInTheDocument(); // space utilization
    // People utilization is null → honest em dash, not 0%.
    expect(screen.getByText("—")).toBeInTheDocument();
    // Admin transparency footer.
    expect(screen.getByText(/Source: live/)).toBeInTheDocument();
  });

  it("shows the loading state while the overview is pending", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: undefined, isLoading: true, error: null });
    render(<OverviewTab />);
    expect(screen.getByText("Loading insights…")).toBeInTheDocument();
  });

  it("shows an error message when the overview fails", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: undefined, isLoading: false, error: new Error("boom") });
    render(<OverviewTab />);
    expect(screen.getByText(/Could not load insights/)).toBeInTheDocument();
  });

  it("renders the request-status chart empty state when the series is empty", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: overviewData(), isLoading: false, error: null });
    (useInsightsRequests as Mock).mockReturnValue({
      data: { bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });

    render(<OverviewTab />);

    expect(screen.getByText("No scheduled requests in this period.")).toBeInTheDocument();
  });
});

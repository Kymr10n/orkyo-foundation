import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen } from "@testing-library/react";
import { InsightsTab } from "./InsightsTab";
import {
  useInsightsConflicts,
  useInsightsOverview,
  useInsightsRequests,
  useInsightsUtilization,
} from "@foundation/src/hooks/useInsights";
import type { InsightsOverview } from "@foundation/src/lib/api/insights-api";

// Site comes from the global store — pin it to "all sites".
vi.mock("@foundation/src/store/app-store", () => ({
  useAppStore: (selector: (s: { selectedSiteId: string | null }) => unknown) =>
    selector({ selectedSiteId: null }),
}));

// Recharts needs a real layout box (absent in happy-dom); stub to passthroughs so the
// component's own scaffolding (titles, empty states, KPI values) is what we assert.
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
  useInsightsUtilization: vi.fn(),
  useInsightsConflicts: vi.fn(),
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
  (useInsightsUtilization as Mock).mockReturnValue(idle);
  (useInsightsConflicts as Mock).mockReturnValue(idle);
  (useInsightsRequests as Mock).mockReturnValue(idle);
});

describe("InsightsTab", () => {
  it("renders KPI cards from overview data", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: overviewData(), isLoading: false, error: null });

    render(<InsightsTab />);

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
    render(<InsightsTab />);
    expect(screen.getByText("Loading insights…")).toBeInTheDocument();
  });

  it("shows an error message when the overview fails", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: undefined, isLoading: false, error: new Error("boom") });
    render(<InsightsTab />);
    expect(screen.getByText(/Could not load insights/)).toBeInTheDocument();
  });

  it("renders chart empty states when series are empty", () => {
    (useInsightsOverview as Mock).mockReturnValue({ data: overviewData(), isLoading: false, error: null });
    (useInsightsUtilization as Mock).mockReturnValue({
      data: { resourceType: "space", bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });
    (useInsightsConflicts as Mock).mockReturnValue({
      data: { bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });
    (useInsightsRequests as Mock).mockReturnValue({
      data: { bucket: "month", series: [], metadata: { calculatedAt: "x", sourceMode: "live" } },
      isLoading: false, error: null,
    });

    render(<InsightsTab />);

    expect(screen.getByText("No scheduled requests in this period.")).toBeInTheDocument();
    expect(screen.getByText("No conflicts in this period.")).toBeInTheDocument();
    expect(screen.getAllByText("No capacity configured for this period.")).toHaveLength(2); // space + people
  });
});

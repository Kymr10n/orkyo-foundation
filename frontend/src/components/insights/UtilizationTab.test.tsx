import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { UtilizationTab } from "./UtilizationTab";
import { getInsightsUtilization } from "@foundation/src/lib/api/insights-api";
import type { InsightsUtilization } from "@foundation/src/lib/api/insights-api";

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

vi.mock("@foundation/src/lib/api/insights-api", () => ({ getInsightsUtilization: vi.fn() }));

// The chart list is driven by the tenant's resource types.
vi.mock("@foundation/src/hooks/useResourceTypes", () => ({
  useResourceTypes: () => ({
    data: [
      { id: "rt-space", key: "space", displayName: "Space", displayNamePlural: "Spaces", isSystem: true, isActive: true },
      { id: "rt-person", key: "person", displayName: "Person", displayNamePlural: "People", isSystem: true, isActive: true },
      { id: "rt-vehicle", key: "vehicle", displayName: "Vehicle", displayNamePlural: "Vehicles", isSystem: false, isActive: true },
    ],
  }),
}));

/** A response for one type, with however many resources stand behind it. */
function response(resourceType: string, resourceCount: number): InsightsUtilization {
  return {
    resourceType,
    bucket: "month",
    series: [],
    resourceCount,
    metadata: { calculatedAt: "x", sourceMode: "live" },
  } as InsightsUtilization;
}

function renderTab() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <UtilizationTab />
    </QueryClientProvider>,
  );
}

beforeEach(() => vi.clearAllMocks());

describe("UtilizationTab", () => {
  it("renders one chart per type the site holds, including tenant-defined ones", async () => {
    (getInsightsUtilization as Mock).mockImplementation((type: string) =>
      Promise.resolve(response(type, 3)),
    );

    renderTab();

    expect(await screen.findByText("Spaces utilization trend")).toBeInTheDocument();
    expect(screen.getByText("People utilization trend")).toBeInTheDocument();
    // Previously impossible: the chart list was two hard-coded hook calls.
    expect(screen.getByText("Vehicles utilization trend")).toBeInTheDocument();
  });

  it("draws no card for a type the site has none of", async () => {
    // A frame reading "no capacity configured" over a site with no vehicles sends the reader
    // hunting for a setting, and can never fill in while that site is selected.
    (getInsightsUtilization as Mock).mockImplementation((type: string) =>
      Promise.resolve(response(type, type === "vehicle" ? 0 : 2)),
    );

    renderTab();

    expect(await screen.findByText("Spaces utilization trend")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByText("Vehicles utilization trend")).not.toBeInTheDocument(),
    );
  });

  it("still reports zero capacity when the resources are there", async () => {
    // Resources present, capacity nets to zero — the message is true here, and stays.
    (getInsightsUtilization as Mock).mockImplementation((type: string) =>
      Promise.resolve(response(type, 4)),
    );

    renderTab();

    // The empty message renders once the series resolves, so wait for it rather than the title.
    await waitFor(() =>
      expect(screen.getAllByText("No capacity configured for this period.")).toHaveLength(3),
    );
  });

  it("says so when the site holds nothing at all", async () => {
    (getInsightsUtilization as Mock).mockImplementation((type: string) =>
      Promise.resolve(response(type, 0)),
    );

    renderTab();

    expect(await screen.findByText(/This site has no resources yet/)).toBeInTheDocument();
  });

  it("keeps a card while its query is in flight, so the grid does not reflow", () => {
    (getInsightsUtilization as Mock).mockReturnValue(new Promise(() => {}));

    renderTab();

    expect(screen.getByText("Spaces utilization trend")).toBeInTheDocument();
    expect(screen.getByText("Vehicles utilization trend")).toBeInTheDocument();
  });

  it("keeps every card when the API does not report a count", async () => {
    // An API build older than the field returns no `resourceCount`. Hiding on a missing number
    // would blank the page over version skew alone.
    (getInsightsUtilization as Mock).mockImplementation((type: string) => {
      const r = response(type, 0) as Partial<InsightsUtilization>;
      delete r.resourceCount;
      return Promise.resolve(r);
    });

    renderTab();

    expect(await screen.findByText("Spaces utilization trend")).toBeInTheDocument();
    expect(screen.getByText("Vehicles utilization trend")).toBeInTheDocument();
    expect(screen.queryByText(/This site has no resources yet/)).not.toBeInTheDocument();
  });
});

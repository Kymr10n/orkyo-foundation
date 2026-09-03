import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BottlenecksTab } from "./BottlenecksTab";
import { getInsightsBottlenecks } from "@foundation/src/lib/api/insights-api";
import { getCriticalPath } from "@foundation/src/lib/api/request-dependency-api";
import { getRequest } from "@foundation/src/lib/api/request-api";

vi.mock("@foundation/src/components/insights/insightsTabContext", () => ({
  useInsightsTabContext: () => ({
    from: new Date("2026-01-01"),
    to: new Date("2026-12-31"),
    bucket: "month",
    siteId: null,
  }),
}));

vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: React.ReactNode }) => <div>{children}</div>;
  const Noop = () => null;
  return {
    ResponsiveContainer: Pass, BarChart: Pass, LineChart: Pass,
    Bar: Noop, Line: Noop, XAxis: Noop, YAxis: Noop, Tooltip: Noop, Legend: Noop, CartesianGrid: Noop,
  };
});

vi.mock("@foundation/src/lib/api/insights-api", () => ({ getInsightsBottlenecks: vi.fn() }));

// Two types is the point of the tab: one ranking each, so a busy type cannot crowd out the other.
vi.mock("@foundation/src/hooks/useResourceTypes", () => ({
  useResourceTypes: () => ({
    data: [
      // hasGeometry is what splits the classes: a station has a fixed location, an asset moves.
      { id: "rt-mill", key: "mill", displayName: "Mill", displayNamePlural: "Mills", hasGeometry: true, isSystem: false, isActive: true },
      { id: "rt-lathe", key: "lathe", displayName: "Lathe", displayNamePlural: "CNC Lathes", hasGeometry: true, isSystem: false, isActive: true },
      { id: "rt-person", key: "person", displayName: "Person", displayNamePlural: "People", hasGeometry: false, isSystem: true, isActive: true },
      { id: "rt-tool", key: "tool", displayName: "Tool", displayNamePlural: "Tools", hasGeometry: false, isSystem: false, isActive: true },
    ],
  }),
}));
vi.mock("@foundation/src/lib/api/request-dependency-api", () => ({ getCriticalPath: vi.fn() }));
vi.mock("@foundation/src/lib/api/request-api", () => ({ getRequest: vi.fn() }));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

// The editor hook reaches for auth/tenant context this suite has no business standing up; the
// tab's contract here is "asks the editor to open the right request", which the spy captures.
const mockOpen = vi.fn();
vi.mock("@foundation/src/components/requests/useRequestEditor", () => ({
  useRequestEditor: () => ({ open: mockOpen, dialogs: <div data-testid="request-dialogs" /> }),
}));

const conflictsByRequest = new Map<string, unknown[]>();
vi.mock("@foundation/src/hooks/useConflictRegistry", () => ({
  useConflictRegistry: () => ({ conflictsByRequest }),
}));

const emptyBottlenecks = {
  period: { from: "2026-01-01", to: "2026-12-31" },
  siteId: null,
  items: [],
  metadata: { calculatedAt: "2026-01-01T00:00:00Z", sourceMode: "live" },
};

const emptyPath = { nodes: [], edges: [], durationDays: 0, diagnostics: [] };

function node(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    requestId: "r1",
    name: "Mill the bracket",
    earliestStart: "2026-06-01",
    earliestFinish: "2026-06-02",
    latestStart: "2026-06-01",
    latestFinish: "2026-06-02",
    totalFloatDays: 0,
    isCritical: true,
    isScheduled: false,
    ...overrides,
  };
}

function renderTab() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <BottlenecksTab />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  conflictsByRequest.clear();
  (getInsightsBottlenecks as Mock).mockResolvedValue(emptyBottlenecks);
  (getCriticalPath as Mock).mockResolvedValue(emptyPath);
  (getRequest as Mock).mockResolvedValue({ id: "r1", name: "Mill the bracket" });
});

describe("BottlenecksTab", () => {
  it("charts stations and assets separately so one class cannot swamp the other", async () => {
    // The reported gap: ranked together, people filled all ten slots and the stations a planner
    // needs to see never appeared.
    const item = (name: string, key: string, minutes: number) => ({
      resourceId: name, name, resourceTypeKey: key,
      resourceTypeDisplayName: key, overbookedMinutes: minutes,
      capacityMinutes: 44640, peakUtilizationPercent: 150,
    });
    (getInsightsBottlenecks as Mock).mockImplementation((_f, _t, _s, type) =>
      Promise.resolve({
        ...emptyBottlenecks,
        items: type === "person" ? [item("Justine", "person", 2160)]
          : type === "mill" ? [item("Mill 1", "mill", 120)]
          : [],
      }),
    );

    renderTab();

    // Both cards use the neutral word while they mix types — the class distinction is on
    // the filter control, so a list of people is never headed "Most overloaded assets".
    await waitFor(() =>
      expect(screen.getAllByText("Most overloaded resources")).toHaveLength(2),
    );
    // Every type is fetched, so narrowing later reads from cache.
    const asked = (getInsightsBottlenecks as Mock).mock.calls.map((c) => c[3]);
    expect(asked).toEqual(expect.arrayContaining(["mill", "lathe", "person", "tool"]));
  });

  it("narrows a class to one of its resource types, named as the workspace wrote it", async () => {
    (getInsightsBottlenecks as Mock).mockResolvedValue(emptyBottlenecks);
    renderTab();

    await waitFor(() =>
      expect(screen.getAllByText("Most overloaded resources")).toHaveLength(2),
    );
    await userEvent.click(screen.getByRole("combobox", { name: /filter stations/i }));
    await userEvent.click(await screen.findByRole("option", { name: "CNC Lathes" }));

    // Verbatim: lowercasing a tenant-authored name turns "CNC Lathes" into "cnc lathes".
    await waitFor(() =>
      expect(screen.getByText("Most overloaded CNC Lathes")).toBeInTheDocument(),
    );
  });

  it("points at the Dependencies tab when no request depends on another", async () => {
    renderTab();

    // The empty state has to say how to make it non-empty, or it reads as a broken feature.
    await waitFor(() =>
      expect(screen.getByText(/nothing depends on anything yet/i)).toBeInTheDocument(),
    );
  });

  it("marks the critical work and shows float for the rest", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...emptyPath,
      durationDays: 6,
      nodes: [
        node(),
        node({ requestId: "r2", name: "Grind", totalFloatDays: 3, isCritical: false, isScheduled: true }),
      ],
    });

    renderTab();

    await waitFor(() => expect(screen.getByText("Mill the bracket")).toBeInTheDocument());
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText("Scheduled")).toBeInTheDocument();
    expect(screen.getByText("3 d")).toBeInTheDocument();
    expect(screen.getByText(/6 days end to end/i)).toBeInTheDocument();
  });

  it("opens the request behind a critical-path row, with its conflicts", async () => {
    // The row names a request but the node carries only an id — the point of the click is that
    // the user can act on the work the table just told them is holding up the finish date.
    const request = { id: "r1", name: "Mill the bracket" };
    (getRequest as Mock).mockResolvedValue(request);
    const conflicts = [{ id: "c1", kind: "dependency_violation" }];
    conflictsByRequest.set("r1", conflicts);
    (getCriticalPath as Mock).mockResolvedValue({ ...emptyPath, nodes: [node()] });

    renderTab();
    await waitFor(() => expect(screen.getByText("Mill the bracket")).toBeInTheDocument());
    await userEvent.click(screen.getByText("Mill the bracket").closest('[role="button"]')!);

    await waitFor(() => expect(mockOpen).toHaveBeenCalledWith(request, conflicts));
    expect(getRequest).toHaveBeenCalledWith("r1");
  });

  it("opens the row from the keyboard, not just the mouse", async () => {
    (getCriticalPath as Mock).mockResolvedValue({ ...emptyPath, nodes: [node()] });

    renderTab();
    await waitFor(() => expect(screen.getByText("Mill the bracket")).toBeInTheDocument());
    const row = screen.getByText("Mill the bracket").closest('[role="button"]') as HTMLElement;
    expect(row).toHaveAttribute("tabIndex", "0");
    row.focus();
    await userEvent.keyboard("{Enter}");

    await waitFor(() => expect(mockOpen).toHaveBeenCalled());
  });

  it("says so rather than doing nothing when the request cannot be fetched", async () => {
    (getRequest as Mock).mockRejectedValue(new Error("boom"));
    (getCriticalPath as Mock).mockResolvedValue({ ...emptyPath, nodes: [node()] });

    renderTab();
    await waitFor(() => expect(screen.getByText("Mill the bracket")).toBeInTheDocument());
    await userEvent.click(screen.getByText("Mill the bracket").closest('[role="button"]')!);

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(mockOpen).not.toHaveBeenCalled();
  });

  it("surfaces diagnostics rather than hiding them", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...emptyPath,
      nodes: [node()],
      diagnostics: ["2 dependency edge(s) reference requests outside this scope and were excluded."],
    });

    renderTab();

    await waitFor(() =>
      expect(screen.getByText(/reference requests outside this scope/i)).toBeInTheDocument(),
    );
  });

  it("reports a failed computation instead of an empty table", async () => {
    (getCriticalPath as Mock).mockRejectedValue(new Error("cycle"));

    renderTab();

    await waitFor(() =>
      expect(screen.getByText(/could not compute the critical path/i)).toBeInTheDocument(),
    );
  });
});

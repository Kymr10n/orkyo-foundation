import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { BottlenecksTab } from "./BottlenecksTab";
import { getInsightsBottlenecks } from "@foundation/src/lib/api/insights-api";
import { getCriticalPath } from "@foundation/src/lib/api/request-dependency-api";
import { getRequest } from "@foundation/src/lib/api/request-api";
import { renderWithQuery } from "@foundation/src/test-utils";

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

// The editor hook reaches for auth/tenant context this suite has no business standing up; the
// tab's contract here is "asks the editor to open the right request", which the spy captures.
const mockOpen = vi.fn();
vi.mock("@foundation/src/hooks/useRequestEditor", () => ({
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

const emptyPath = { nodes: [], edges: [], chains: [], diagnostics: [] };

function node(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    requestId: "r1",
    name: "Mill the bracket",
    earliestStart: "2026-06-01T08:00:00Z",
    earliestFinish: "2026-06-01T11:20:00Z",
    latestStart: "2026-06-01T08:00:00Z",
    latestFinish: "2026-06-01T11:20:00Z",
    totalFloatMinutes: 0,
    isCritical: true,
    isScheduled: false,
    chainId: "c1",
    ...overrides,
  };
}

function chain(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    chainId: "c1",
    requestIds: ["r1"],
    firstName: "Mill the bracket",
    lastName: "Mill the bracket",
    start: "2026-06-01T08:00:00Z",
    finish: "2026-06-01T11:20:00Z",
    deadline: null,
    slackMinutes: null,
    ...overrides,
  };
}

/** One chain holding the given steps, the shape the server returns for a single linked group. */
function pathOf(nodes: ReturnType<typeof node>[], overrides: Partial<Record<string, unknown>> = {}) {
  return {
    ...emptyPath,
    nodes,
    chains: [chain({ requestIds: nodes.map((n) => n.requestId), lastName: nodes.at(-1)?.name })],
    ...overrides,
  };
}

/** Steps sit behind their chain row, so a test that clicks a step opens the chain first. */
async function expandChain() {
  await waitFor(() => expect(screen.getByRole("button", { expanded: false })).toBeInTheDocument());
  await userEvent.click(screen.getByRole("button", { expanded: false }));
}

function renderTab() {
  return renderWithQuery(<BottlenecksTab />);
}

beforeEach(() => {
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
      expect(screen.getByText(/no open work depends on anything/i)).toBeInTheDocument(),
    );
  });

  it("lists one row per chain and shows its steps on expand", async () => {
    (getCriticalPath as Mock).mockResolvedValue(
      pathOf([
        node(),
        node({ requestId: "r2", name: "Grind", totalFloatMinutes: 3 * 1440 + 120, isCritical: false, isScheduled: true }),
      ]),
    );

    renderTab();

    await waitFor(() => expect(screen.getByText("Mill the bracket → Grind")).toBeInTheDocument());
    expect(screen.getByText("2 steps")).toBeInTheDocument();
    expect(screen.getByText("1 in this period")).toBeInTheDocument();
    expect(screen.queryByText("Critical")).not.toBeInTheDocument();

    await expandChain();

    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText("Scheduled")).toBeInTheDocument();
    expect(screen.getByText("3d 2h")).toBeInTheDocument();
  });

  it("keeps the server's risk order and flags a chain that misses its deadline", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...emptyPath,
      nodes: [node(), node({ requestId: "r2", name: "Paint", chainId: "c2" })],
      chains: [
        chain({ chainId: "c1", firstName: "Late", lastName: "Late end", deadline: "2026-06-01T10:00:00Z", slackMinutes: -1500 }),
        chain({ chainId: "c2", requestIds: ["r2"], firstName: "Easy", lastName: "Easy end", deadline: "2026-06-09T10:00:00Z", slackMinutes: 2 * 1440 }),
      ],
    });

    renderTab();

    await waitFor(() => expect(screen.getByText("Late by 1d 1h")).toBeInTheDocument());
    expect(screen.getByText("2d")).toBeInTheDocument();
    const rows = screen.getAllByRole("button", { expanded: false }).map((b) => b.textContent);
    expect(rows[0]).toContain("Late → Late end");
    expect(rows[1]).toContain("Easy → Easy end");
  });

  it("lists only the chains that overlap the selected period", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...emptyPath,
      nodes: [node(), node({ requestId: "r2", name: "Next year", chainId: "c2" })],
      chains: [
        chain(),
        chain({ chainId: "c2", requestIds: ["r2"], firstName: "Next year", lastName: "Next year", start: "2027-03-01T08:00:00Z", finish: "2027-03-02T08:00:00Z" }),
      ],
    });

    renderTab();

    await waitFor(() => expect(screen.getByText("1 in this period")).toBeInTheDocument());
    expect(screen.queryByText(/Next year →/)).not.toBeInTheDocument();
  });

  it("says when no chain falls in the period", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...pathOf([node()]),
      chains: [chain({ start: "2027-03-01T08:00:00Z", finish: "2027-03-02T08:00:00Z" })],
    });

    renderTab();

    await waitFor(() =>
      expect(screen.getByText(/no open dependency chains in this period/i)).toBeInTheDocument(),
    );
  });

  it("opens the request behind a critical-path row, with its conflicts", async () => {
    // The row names a request but the node carries only an id — the point of the click is that
    // the user can act on the work the table just told them is holding up the finish date.
    const request = { id: "r1", name: "Mill the bracket" };
    (getRequest as Mock).mockResolvedValue(request);
    const conflicts = [{ id: "c1", kind: "dependency_violation" }];
    conflictsByRequest.set("r1", conflicts);
    (getCriticalPath as Mock).mockResolvedValue(pathOf([node()]));

    renderTab();
    await expandChain();
    await userEvent.click(screen.getAllByText("Mill the bracket").at(-1)!.closest('tr[role="button"]')!);

    await waitFor(() => expect(mockOpen).toHaveBeenCalledWith(request, conflicts));
    expect(getRequest).toHaveBeenCalledWith("r1");
  });

  it("opens the row from the keyboard, not just the mouse", async () => {
    (getCriticalPath as Mock).mockResolvedValue(pathOf([node()]));

    renderTab();
    await expandChain();
    const row = screen.getAllByText("Mill the bracket").at(-1)!.closest('tr[role="button"]') as HTMLElement;
    expect(row).toHaveAttribute("tabIndex", "0");
    row.focus();
    await userEvent.keyboard("{Enter}");

    await waitFor(() => expect(mockOpen).toHaveBeenCalled());
  });

  it("says so rather than doing nothing when the request cannot be fetched", async () => {
    (getRequest as Mock).mockRejectedValue(new Error("boom"));
    (getCriticalPath as Mock).mockResolvedValue(pathOf([node()]));

    renderTab();
    await expandChain();
    await userEvent.click(screen.getAllByText("Mill the bracket").at(-1)!.closest('tr[role="button"]')!);

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(mockOpen).not.toHaveBeenCalled();
  });

  it("surfaces diagnostics rather than hiding them", async () => {
    (getCriticalPath as Mock).mockResolvedValue({
      ...pathOf([node()]),
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
      expect(screen.getByText(/could not compute the dependency chains/i)).toBeInTheDocument(),
    );
  });
});

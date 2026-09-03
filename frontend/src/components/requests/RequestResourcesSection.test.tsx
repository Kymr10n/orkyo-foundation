import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestResourcesSection } from "./RequestResourcesSection";
import { Tabs } from "@foundation/src/components/ui/tabs";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { getUtilizationByResource } from "@foundation/src/lib/api/resource-utilization-api";

vi.mock("@foundation/src/lib/api/resources-api", () => ({ getResources: vi.fn() }));
vi.mock("@foundation/src/lib/api/resource-utilization-api", () => ({
  getUtilizationByResource: vi.fn(),
}));

// The People section runs its own loads and validation; none of that is under test here.
vi.mock("./RequestPeopleSection", () => ({ RequestPeopleSection: () => null }));
vi.mock("./RequestTargetTypesField", () => ({ RequestTargetTypesField: () => null }));

vi.mock("@foundation/src/hooks/useResourceTypes", () => ({
  useResourceTypes: () => ({
    data: [
      {
        id: "rt-mill",
        key: "mill",
        displayName: "Mill",
        displayNamePlural: "Mills",
        hasGeometry: true,
        hasDirectoryProfile: false,
        isSystem: false,
        isActive: true,
      },
    ],
  }),
}));

const MILLS = {
  data: [
    { id: "m-1", name: "PMF Mill VMC-1" },
    { id: "m-2", name: "PMF Mill VMC-2" },
    { id: "m-3", name: "PMF Mill VMC-3" },
    { id: "m-4", name: "PMF Mill VMC-4" },
  ],
};

function bucket(over: Partial<Record<string, unknown>> = {}) {
  return {
    start: "2026-09-08T08:00:00Z",
    end: "2026-09-08T09:00:00Z",
    allocatedPercent: 0,
    effectiveAvailabilityPercent: 100,
    isExclusiveOccupied: false,
    ...over,
  };
}

function renderSection(withWindow = true) {
  const state = {
    targetResourceTypeKeys: ["mill"],
    selectedResourceIds: {},
    startDate: withWindow ? "2026-09-08" : "",
    startTime: withWindow ? "08:00" : "",
    endDate: withWindow ? "2026-09-08" : "",
    endTime: withWindow ? "12:00" : "",
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      {/* The section renders a TabsContent, so it needs the Radix Tabs context. */}
      <Tabs value="resources">
      <RequestResourcesSection
        activeTab="resources"
        state={state as never}
        setField={vi.fn() as never}
        readOnly={false}
        requestId="req-1"
        siteId="site-1"
        hasEditableSchedule
        onBlockersChange={vi.fn()}
        conflictsByResourceId={new Map()}
      />
      </Tabs>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (getResources as Mock).mockResolvedValue(MILLS);
  (getUtilizationByResource as Mock).mockResolvedValue([
    { resourceId: "m-1", buckets: [bucket({ isExclusiveOccupied: true })] },
    { resourceId: "m-2", buckets: [bucket({ allocatedPercent: 40 })] },
    { resourceId: "m-3", buckets: [bucket()] },
    { resourceId: "m-4", buckets: [bucket({ effectiveAvailabilityPercent: 0 })] },
  ]);
});

describe("RequestResourcesSection availability", () => {
  it("says which resources can take the chosen window", async () => {
    // Without this the planner picks a machine, saves, and only then learns it was taken.
    renderSection();
    await waitFor(() => expect(getUtilizationByResource).toHaveBeenCalled());

    await userEvent.click(await screen.findByRole("combobox", { name: "Mill" }));

    expect(await screen.findByText("Busy")).toBeInTheDocument();
    expect(screen.getByText("Partly booked")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
    expect(screen.getByText("Unavailable")).toBeInTheDocument();
  });

  it("scopes the lookup to the type, site and window", async () => {
    renderSection();

    await waitFor(() => expect(getUtilizationByResource).toHaveBeenCalled());
    const [from, to, granularity, typeKey, siteId] = (getUtilizationByResource as Mock).mock.calls[0];
    expect(from.toISOString()).toContain("2026-09-08");
    expect(to.toISOString()).toContain("2026-09-08");
    // Hourly for a same-day window, so a morning job does not mark the machine busy all day.
    expect(granularity).toBe("hour");
    expect(typeKey).toBe("mill");
    expect(siteId).toBe("site-1");
  });

  it("asks nothing until the request has a window", async () => {
    renderSection(false);

    await waitFor(() => expect(getResources).toHaveBeenCalled());
    expect(getUtilizationByResource).not.toHaveBeenCalled();
  });

  it("still lists resources when the availability lookup is unavailable", async () => {
    // The badges are a hint; losing them must not cost the planner the picker.
    (getUtilizationByResource as Mock).mockRejectedValue(new Error("boom"));
    renderSection();

    await userEvent.click(await screen.findByRole("combobox", { name: "Mill" }));

    expect(await screen.findByText("PMF Mill VMC-1")).toBeInTheDocument();
    expect(screen.queryByText("Free")).not.toBeInTheDocument();
  });
});

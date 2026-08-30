/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

// Mock FullCalendar + plugins so the wrapper's wiring can be tested without the
// real library (and without a browser layout engine). The stub captures the
// props FullCalendar would receive so we can invoke its callbacks directly.
let capturedProps: Record<string, any> = {};
vi.mock("@fullcalendar/react", async () => {
  const { forwardRef } = await import("react");
  return {
    // forwardRef so the component's calendarRef doesn't warn; the ref stays null
    // (no getApi), so the view/date sync effects are inert under test.
    default: forwardRef((props: any, _ref: any) => {
      capturedProps = props;
      return null;
    }),
  };
});
vi.mock("@fullcalendar/daygrid", () => ({ default: {} }));
vi.mock("@fullcalendar/timegrid", () => ({ default: {} }));
vi.mock("@fullcalendar/list", () => ({ default: {} }));
vi.mock("@fullcalendar/interaction", () => ({ default: {} }));
vi.mock("./request-calendar.css", () => ({}));

// Breakpoint is mocked so phone vs desktop view selection is deterministic
// (the real hook reads matchMedia). Defaults to desktop; flip per-test.
let mockIsPhone = false;
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({
  useBreakpoint: () => ({
    isPhone: mockIsPhone,
    isTablet: false,
    isDesktop: !mockIsPhone,
    device: mockIsPhone ? "phone" : "desktop",
  }),
}));

import userEvent from "@testing-library/user-event";
import { RequestCalendar } from "./RequestCalendar";
import type { CalendarEvent } from "./request-calendar-events";

const event: CalendarEvent = {
  id: "r1",
  title: "Task",
  start: "2026-04-17T09:00:00Z",
  end: "2026-04-17T11:00:00Z",
  classNames: ["orkyo-cal-event"],
  editable: true,
  extendedProps: { kind: "request" as const, requestId: "r1", status: "new", conflictSeverity: null },
};

function renderCalendar(overrides: Partial<React.ComponentProps<typeof RequestCalendar>> = {}) {
  const handlers = {
    onEventClick: vi.fn(),
    onEventMove: vi.fn(),
    onEventResize: vi.fn(),
    onSlotSelect: vi.fn(),
    onDatesSet: vi.fn(),
  };
  render(
    <RequestCalendar
      events={[event]}
      editable
      initialView="timeGridWeek"
      initialDate={new Date("2026-04-17T00:00:00Z")}
      active
      {...handlers}
      {...overrides}
    />,
  );
  return handlers;
}

beforeEach(() => {
  capturedProps = {};
  mockIsPhone = false;
});

describe("RequestCalendar", () => {
  it("forwards events and the editable flag to FullCalendar", () => {
    renderCalendar();
    expect(capturedProps.events).toHaveLength(1);
    expect(capturedProps.editable).toBe(true);
    expect(capturedProps.selectable).toBe(true);
    expect(capturedProps.initialView).toBe("timeGridWeek");
  });

  it("partitions overlapping events side by side, in a stable column order", () => {
    // slotEventOverlap: FullCalendar's default stretches overlap columns across half
    // their neighbour and paints the later event on top — at 16px slots that buried
    // short events entirely. eventOrder keeps columns from shuffling on re-render.
    renderCalendar();
    expect(capturedProps.slotEventOverlap).toBe(false);
    expect(capturedProps.eventOrder).toBe("start,-duration,title");
  });

  it("is page-controlled: forwards the resolved view and disables FC's toolbar", () => {
    // The view is resolved upstream (scaleToCalendarView + breakpoint) and passed
    // in; the component forwards it verbatim and turns off FullCalendar's own
    // toolbar so the page's scale selector + date navigator are the only controls.
    renderCalendar({ initialView: "dayGridMonth" });
    expect(capturedProps.initialView).toBe("dayGridMonth");
    expect(capturedProps.headerToolbar).toBe(false);
  });

  it("forwards a phone list view unchanged (no in-component remapping)", () => {
    mockIsPhone = true;
    renderCalendar({ initialView: "listWeek" });
    expect(capturedProps.initialView).toBe("listWeek");
    expect(capturedProps.headerToolbar).toBe(false);
  });

  it("localizes date/time formatting to the user's browser locale", () => {
    renderCalendar();
    // Driven by navigator.language (not FullCalendar's hardcoded `en` default).
    // The inline { code } form formats via Intl without bundling all locale packs.
    expect(capturedProps.locale).toEqual({ code: navigator.language });
  });

  it("disables interaction for read-only users", () => {
    renderCalendar({ editable: false });
    expect(capturedProps.editable).toBe(false);
    expect(capturedProps.selectable).toBe(false);
  });

  it("maps eventClick to the request id", () => {
    const { onEventClick } = renderCalendar();
    capturedProps.eventClick({ event: { id: "r1" } });
    expect(onEventClick).toHaveBeenCalledWith("r1");
  });

  it("forwards a drag with both bounds as a move", () => {
    const { onEventMove } = renderCalendar();
    const start = new Date("2026-04-18T09:00:00Z");
    const end = new Date("2026-04-18T11:00:00Z");
    capturedProps.eventDrop({ event: { id: "r1", start, end }, revert: vi.fn() });
    expect(onEventMove).toHaveBeenCalledWith("r1", start, end);
  });

  it("reverts a drag that loses its end bound instead of rescheduling", () => {
    const { onEventMove } = renderCalendar();
    const revert = vi.fn();
    capturedProps.eventDrop({ event: { id: "r1", start: new Date(), end: null }, revert });
    expect(onEventMove).not.toHaveBeenCalled();
    expect(revert).toHaveBeenCalledTimes(1);
  });

  it("forwards a resize as onEventResize", () => {
    const { onEventResize } = renderCalendar();
    const start = new Date("2026-04-17T09:00:00Z");
    const end = new Date("2026-04-17T12:00:00Z");
    capturedProps.eventResize({ event: { id: "r1", start, end }, revert: vi.fn() });
    expect(onEventResize).toHaveBeenCalledWith("r1", start, end);
  });

  it("reverts a resize that loses its end bound", () => {
    const { onEventResize } = renderCalendar();
    const revert = vi.fn();
    capturedProps.eventResize({ event: { id: "r1", start: new Date(), end: null }, revert });
    expect(onEventResize).not.toHaveBeenCalled();
    expect(revert).toHaveBeenCalledTimes(1);
  });

  it("maps an empty-slot selection to onSlotSelect", () => {
    const { onSlotSelect } = renderCalendar();
    const start = new Date("2026-04-17T13:00:00Z");
    const end = new Date("2026-04-17T14:00:00Z");
    capturedProps.select({ start, end });
    expect(onSlotSelect).toHaveBeenCalledWith(start, end);
  });

  it("reports the visible range start on datesSet (anchor sync)", () => {
    const { onDatesSet } = renderCalendar();
    const currentStart = new Date("2026-04-13T00:00:00Z");
    capturedProps.datesSet({ view: { type: "timeGridWeek", currentStart } });
    expect(onDatesSet).toHaveBeenCalledWith(currentStart);
  });

  it("stays silent on datesSet while inactive (hidden tab must not touch the anchor)", () => {
    const { onDatesSet } = renderCalendar({ active: false });
    capturedProps.datesSet({ view: { type: "timeGridWeek", currentStart: new Date() } });
    expect(onDatesSet).not.toHaveBeenCalled();
  });

  // --- Legend ---

  it("renders legend labels for all statuses and conflict indicators", () => {
    renderCalendar();
    expect(screen.getByText("New")).toBeInTheDocument();
    expect(screen.getByText("In Progress")).toBeInTheDocument();
    expect(screen.getByText("Done")).toBeInTheDocument();
    expect(screen.getByText("Canceled")).toBeInTheDocument();
    expect(screen.getByText("Conflicts")).toBeInTheDocument();
    expect(screen.getByText("Warnings")).toBeInTheDocument();
  });

  it("hides the legend on phones (row tint conveys status in the list view)", () => {
    mockIsPhone = true;
    renderCalendar();
    expect(screen.queryByText("New")).toBeNull();
    expect(screen.queryByText("Conflicts")).toBeNull();
    expect(screen.queryByText("Warnings")).toBeNull();
  });

  // --- eventContent ---

  it("passes eventContent to FullCalendar", () => {
    renderCalendar();
    expect(typeof capturedProps.eventContent).toBe("function");
  });

  it("eventContent renders conflict icon for error severity", () => {
    renderCalendar();
    const { container } = render(
      capturedProps.eventContent({
        event: { title: "Broken Task", start: new Date(2026, 3, 17, 9, 0), extendedProps: { conflictSeverity: "error" } },
      }),
    );
    expect(container.querySelector("svg")).toBeTruthy();
    expect(container.textContent).toContain("Broken Task");
    expect(container.textContent).toContain("09:00");
  });

  it("eventContent renders warning icon for warning severity", () => {
    renderCalendar();
    const { container } = render(
      capturedProps.eventContent({
        event: { title: "Warn Task", start: null, extendedProps: { conflictSeverity: "warning" } },
      }),
    );
    expect(container.querySelector("svg")).toBeTruthy();
  });

  it("eventContent renders no icon when conflictSeverity is null", () => {
    renderCalendar();
    const { container } = render(
      capturedProps.eventContent({
        event: { title: "Fine Task", start: new Date(2026, 3, 17, 10, 0), extendedProps: { conflictSeverity: null } },
      }),
    );
    expect(container.querySelector("svg")).toBeNull();
    expect(container.textContent).toContain("Fine Task");
  });

  it("eventContent defers to FullCalendar's native row in list (agenda) views", () => {
    renderCalendar();
    // Returning true tells FullCalendar to render its default list row (time
    // column + full title) instead of the compact grid-cell layout.
    const result = capturedProps.eventContent({
      view: { type: "listWeek" },
      event: { title: "Agenda Task", start: new Date(2026, 3, 17, 9, 0), extendedProps: { conflictSeverity: "error" } },
    });
    expect(result).toBe(true);
  });

  describe("search and filters", () => {
    const weld: CalendarEvent = {
      ...event,
      id: "r2",
      title: "Finish weld",
      extendedProps: { kind: "request" as const, requestId: "r2", status: "done", conflictSeverity: "error" },
    };

    it("narrows the events FullCalendar receives to the search", async () => {
      renderCalendar({ events: [event, weld] });
      expect(capturedProps.events).toHaveLength(2);

      await userEvent.type(screen.getByLabelText("Search requests"), "weld");

      expect(capturedProps.events).toHaveLength(1);
      expect(capturedProps.events[0].title).toBe("Finish weld");
    });

    it("narrows by status", async () => {
      renderCalendar({ events: [event, weld] });

      await userEvent.click(screen.getByRole("button", { name: "Filter by status" }));
      await userEvent.click(await screen.findByRole("menuitem", { name: "Done" }));

      expect(capturedProps.events.map((e: CalendarEvent) => e.title)).toEqual(["Task"]);
    });

    it("keeps off-time shading regardless of the filter", async () => {
      renderCalendar({
        events: [event],
        offTimeRanges: [{ id: "o1", startMs: 0, endMs: 1000, title: "Closed", resourceIds: null }],
      });

      await userEvent.type(screen.getByLabelText("Search requests"), "nothing matches");

      // The week's shape is not a request; hiding it would redraw the calendar as open time.
      expect(capturedProps.events).toHaveLength(1);
      expect(capturedProps.events[0].display).toBe("background");
    });

    it("keeps the search on phones, where the legend is hidden", () => {
      mockIsPhone = true;
      renderCalendar();

      expect(screen.getByLabelText("Search requests")).toBeInTheDocument();
      expect(screen.queryByText("Conflicts")).not.toBeInTheDocument();
    });
  });
});

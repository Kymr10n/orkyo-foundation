/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

// Mock FullCalendar + plugins so the wrapper's wiring can be tested without the
// real library (and without a browser layout engine). The stub captures the
// props FullCalendar would receive so we can invoke its callbacks directly.
let capturedProps: Record<string, any> = {};
// The imperative API FullCalendar would expose. Null by default, which leaves the ref null and
// every imperative effect (view/date sync, scroll) inert — set it in a test that drives one.
let mockApi: Record<string, any> | null = null;
vi.mock("@fullcalendar/react", async () => {
  const { forwardRef, useImperativeHandle } = await import("react");
  return {
    default: forwardRef((props: any, ref: any) => {
      capturedProps = props;
      useImperativeHandle(ref, () => (mockApi ? { getApi: () => mockApi } : null), []);
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
  mockApi = null;
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

  describe("working-hours window", () => {
    // The pane is the element carrying the fitted slot height; FullCalendar itself is stubbed out.
    const pane = () => document.querySelector<HTMLElement>(".orkyo-calendar div.flex-1");
    const working = { enabled: true, start: "06:00", end: "14:00" };

    it("opens an hour before the working day starts", () => {
      renderCalendar({ workingHours: working });

      expect(capturedProps.scrollTime).toBe("5:00:00");
    });

    it("does not scroll past the edges of the day", () => {
      renderCalendar({ workingHours: { enabled: true, start: "00:00", end: "23:30" } });

      expect(capturedProps.scrollTime).toBe("0:00:00");
    });

    it("leaves FullCalendar's own scroll position when working hours are off", () => {
      renderCalendar({ workingHours: { ...working, enabled: false } });

      expect(capturedProps.scrollTime).toBeUndefined();
    });

    it("leaves the scroll position alone for hosts that pass no working hours", () => {
      // The resource schedule dialog reuses this calendar without scheduling settings.
      renderCalendar();

      expect(capturedProps.scrollTime).toBeUndefined();
    });

    it("ignores working hours that do not describe a day", () => {
      renderCalendar({ workingHours: { enabled: true, start: "17:00", end: "09:00" } });

      expect(capturedProps.scrollTime).toBeUndefined();
      expect(pane()?.style.getPropertyValue("--orkyo-slot-height")).toBe("");
    });

    it("re-applies the opening scroll once the rows are sized", () => {
      // FullCalendar resolves scrollTime into a pixel offset against the slot height at mount —
      // the 16px fallback. Taller rows leave that offset pointing hours earlier, so the view has
      // to be sent back to the working day by time.
      const scrollToTime = vi.fn();
      mockApi = {
        scrollToTime,
        gotoDate: vi.fn(),
        changeView: vi.fn(),
        // The date-sync effect reads the view's period; the anchor sits inside it, so it is quiet.
        view: {
          type: "timeGridWeek",
          currentStart: new Date("2026-04-13T00:00:00Z"),
          currentEnd: new Date("2026-04-20T00:00:00Z"),
        },
      };

      renderCalendar({ workingHours: working });

      expect(scrollToTime).toHaveBeenCalledWith("5:00:00");
    });

    it("sizes the slots so the working window fills the pane", () => {
      renderCalendar({ workingHours: working });

      // 800px of observed pane (see the ResizeObserver stub in test/setup.ts) over 05:00-15:00,
      // which is 20 half-hour slots.
      expect(pane()?.style.getPropertyValue("--orkyo-slot-height")).toBe("40px");
    });

    it("never renders less dense than the static fallback", () => {
      // A full day in the same pane works out under 16px a slot, so the floor takes over and the
      // calendar scrolls instead of shrinking the rows.
      renderCalendar({ workingHours: { enabled: true, start: "01:00", end: "23:00" } });

      expect(pane()?.style.getPropertyValue("--orkyo-slot-height")).toBe("16px");
    });

    it("leaves the slot height to the stylesheet without working hours", () => {
      renderCalendar();

      expect(pane()?.style.getPropertyValue("--orkyo-slot-height")).toBe("");
    });
  });

  describe("hover text", () => {
    // FullCalendar builds the event elements itself, so the wrapper sets the tooltip through
    // eventDidMount; the stub never calls it, so drive it the way FullCalendar would.
    function mountEvent(overrides: Record<string, any> = {}) {
      const el = document.createElement("div");
      capturedProps.eventDidMount({
        el,
        event: {
          title: "Weld structural frames",
          start: new Date("2026-04-17T09:00:00Z"),
          end: new Date("2026-04-17T11:00:00Z"),
          display: "auto",
          extendedProps: { status: "new", conflictSeverity: null },
          ...overrides,
        },
      });
      return el;
    }

    it("names the request on hover, which a block too small to read cannot", () => {
      renderCalendar();

      expect(mountEvent().title).toContain("Weld structural frames");
    });

    it("leaves the off-time shading untitled", () => {
      renderCalendar();

      // Weekends and closures are the week's shape, not something to hover for a name.
      expect(mountEvent({ display: "background" }).title).toBe("");
    });

    it("skips an event with no start rather than guessing a time", () => {
      renderCalendar();

      expect(mountEvent({ start: null }).title).toBe("");
    });
  });
});

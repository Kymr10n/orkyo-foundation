/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

// Mock FullCalendar + plugins so the wrapper's wiring can be tested without the
// real library (and without a browser layout engine). The stub captures the
// props FullCalendar would receive so we can invoke its callbacks directly.
let capturedProps: Record<string, any> = {};
vi.mock("@fullcalendar/react", () => ({
  default: (props: any) => {
    capturedProps = props;
    return null;
  },
}));
vi.mock("@fullcalendar/daygrid", () => ({ default: {} }));
vi.mock("@fullcalendar/timegrid", () => ({ default: {} }));
vi.mock("@fullcalendar/interaction", () => ({ default: {} }));
vi.mock("./request-calendar.css", () => ({}));

import { RequestCalendar } from "./RequestCalendar";
import type { CalendarEvent } from "./request-calendar-events";

const event: CalendarEvent = {
  id: "r1",
  title: "Task",
  start: "2026-04-17T09:00:00Z",
  end: "2026-04-17T11:00:00Z",
  classNames: ["orkyo-cal-event"],
  editable: true,
  extendedProps: { requestId: "r1", status: "new", conflictSeverity: null },
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
      {...handlers}
      {...overrides}
    />,
  );
  return handlers;
}

beforeEach(() => {
  capturedProps = {};
});

describe("RequestCalendar", () => {
  it("forwards events and the editable flag to FullCalendar", () => {
    renderCalendar();
    expect(capturedProps.events).toHaveLength(1);
    expect(capturedProps.editable).toBe(true);
    expect(capturedProps.selectable).toBe(true);
    expect(capturedProps.initialView).toBe("timeGridWeek");
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

  it("translates datesSet into a store scale + anchor", () => {
    const { onDatesSet } = renderCalendar();
    const currentStart = new Date("2026-04-13T00:00:00Z");
    capturedProps.datesSet({ view: { type: "timeGridWeek", currentStart } });
    expect(onDatesSet).toHaveBeenCalledWith("week", currentStart);
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
});

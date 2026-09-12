import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import FullCalendar from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/daygrid";
import timeGridPlugin from "@fullcalendar/timegrid";
import interactionPlugin, { type EventResizeDoneArg } from "@fullcalendar/interaction";
import listPlugin from "@fullcalendar/list";
import type { DateSelectArg, EventClickArg, EventDropArg, DatesSetArg, EventInput, BusinessHoursInput } from "@fullcalendar/core";
import { USER_LOCALE, formatCompactTime, GRID_DAY_HEADER_OPTS } from "@foundation/src/lib/formatters";
import type { CalendarEvent, CalendarView, ConflictSeverity } from "./request-calendar-events";
import { calendarEventTooltip, REQUEST_LEGEND } from "./request-calendar-events";
import type { RequestStatus } from "@foundation/src/types/requests";
import { ScheduleFilterBar } from "./ScheduleFilterBar";
import { parseTimeToHour } from "./time-grid-utils";
import {
  DEFAULT_SCHEDULE_FILTER,
  filterCalendarEvents,
  type ScheduleFilter,
} from "./schedule-filter";
import { severityPresentation } from "@foundation/src/components/ui/status-indicator";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { cn } from "@foundation/src/lib/utils";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import "./request-calendar.css";

interface WorkingHours {
  enabled: boolean;
  /** "HH:mm" */
  start: string;
  /** "HH:mm" */
  end: string;
}

interface RequestCalendarProps {
  /** Scheduled-request events (see request-calendar-events.ts). */
  events: CalendarEvent[];
  /** Off-time ranges (weekends + holidays/closures) rendered as background shading. */
  offTimeRanges?: readonly OffTimeRange[];
  /** When set, non-working slots are shaded in time-grid views. */
  workingHours?: WorkingHours;
  /** Admin/editor → interactive; viewers get a read-only calendar. */
  editable: boolean;
  initialView: CalendarView;
  initialDate: Date;
  /** True only while the Calendar tab is showing. The calendar is always mounted
   *  (Radix force-mounts hidden tabs), so gate its imperative sync + range
   *  reporting on this — otherwise a hidden calendar echoes into the shared
   *  anchor while the user is navigating the Spaces/People grid. */
  active: boolean;
  onEventClick: (requestId: string) => void;
  /** Drag (move) — preserves duration. */
  onEventMove: (requestId: string, start: Date, end: Date) => void;
  onEventResize: (requestId: string, start: Date, end: Date) => void;
  /** Empty-slot selection → schedule chooser. */
  onSlotSelect: (start: Date, end: Date) => void;
  /** Fires on range change so the page can keep the store's anchor aligned. */
  onDatesSet: (activeStart: Date) => void;
  /**
   * Key shown above the grid. Defaults to the request statuses plus conflict/warning, which is
   * what the Utilization page wants; a host charting other kinds passes its own.
   */
  legend?: readonly { className: string; label: string }[];
  /** Hides the query/status/issue bar for hosts whose events carry no request status. */
  showFilterBar?: boolean;
}

/** Hours shown either side of the working day (see workingWindow below). */
const WINDOW_MARGIN_HOURS = 1;
const HOURS_PER_DAY = 24;
/** FullCalendar's default slot duration — two slots to the hour. */
const SLOTS_PER_HOUR = 2;
/** The static density request-calendar.css falls back to; also the floor when fitting. */
const MIN_SLOT_PX = 16;

function LegendItem({ className, label }: { className: string; label: string }) {
  return (
    <span className="flex items-center gap-1">
      <span className={`inline-block h-2.5 w-4 rounded-sm border ${className}`} />
      {label}
    </span>
  );
}

/**
 * Themed FullCalendar wrapper for the Utilization → Calendar tab. Owns all
 * FullCalendar wiring; colours/data come from request-calendar-events.ts.
 *
 * The calendar is *controlled* by the page's own scale selector + date navigator
 * (shared with the Spaces/People tabs) — FullCalendar's built-in toolbar is
 * disabled. `view` is the already-resolved view for the current scale+breakpoint
 * (grid on desktop, agenda list on phone; see scaleToCalendarView); `initialDate`
 * is the current anchor. Effects push both into FullCalendar's imperative API,
 * and `onDatesSet` reports the visible range's start back so the store's anchor
 * stays aligned when the calendar snaps to a period boundary.
 */
export function RequestCalendar({
  events,
  offTimeRanges,
  workingHours,
  editable,
  initialView,
  initialDate,
  active,
  onEventClick,
  onEventMove,
  onEventResize,
  onSlotSelect,
  onDatesSet,
  legend = REQUEST_LEGEND,
  showFilterBar = true,
}: RequestCalendarProps) {
  const plugins = useMemo(() => [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin], []);

  // Legend + list-view styling still branch on breakpoint; the view itself is
  // resolved upstream (page passes the phone-mapped list view directly).
  const { isPhone } = useBreakpoint();

  // FullCalendar reads initialView/initialDate only at mount, so drive later
  // changes through its imperative API. Guarded so we only act on a real change
  // (and gotoDate only when the anchor left the visible range) — otherwise the
  // onDatesSet → setAnchorTs → prop round-trip would loop.
  const calendarRef = useRef<FullCalendar>(null);

  useEffect(() => {
    if (!active) return;
    const api = calendarRef.current?.getApi();
    if (api && api.view.type !== initialView) api.changeView(initialView);
  }, [active, initialView]);

  useEffect(() => {
    if (!active) return;
    const api = calendarRef.current?.getApi();
    if (!api) return;
    // Compare against the view's *period* (currentStart/currentEnd), NOT the
    // padded visible range (activeStart/activeEnd). The month grid renders
    // trailing days of the next month, so a one-month step lands inside the
    // padded range and would be wrongly suppressed; the period bounds step
    // correctly while still ignoring the datesSet → setAnchorTs echo.
    const t = initialDate.getTime();
    if (t < api.view.currentStart.getTime() || t >= api.view.currentEnd.getTime()) {
      api.gotoDate(initialDate);
    }
  }, [active, initialDate]);

  // Format dates/times (slot labels, day headers, event times, title) per the
  // user's browser locale — e.g. 24-hour "06:00" vs 12-hour "6 AM", and locale
  // date ordering — instead of FullCalendar's hardcoded `en` default. The inline
  // `{ code }` form formats via Intl without bundling all locale packs (and without
  // FullCalendar's "unknown locale" warning); buttonText + firstDay below stay fixed.
  const locale = useMemo(() => ({ code: USER_LOCALE }), []);

  const businessHoursConfig = useMemo<BusinessHoursInput | false>(() => {
    if (!workingHours?.enabled) return false;
    return { startTime: workingHours.start, endTime: workingHours.end };
  }, [workingHours]);

  // The hours worth looking at: the site's working day plus an hour either side, so an early
  // start or an overrun is visible without scrolling. The axis itself stays 00:00–24:00 (no
  // slotMin/MaxTime) — nothing is ever hidden — but the view opens here and the slots are sized
  // so this window fills the pane instead of the ~two thirds of empty night the day used to
  // spend. Null when the tenant has working hours switched off, or the times are unusable; then
  // the calendar keeps FullCalendar's own defaults, which is also what the resource schedule
  // dialog (no workingHours prop) gets.
  const workingWindow = useMemo(() => {
    if (!workingHours?.enabled) return null;
    const start = parseTimeToHour(workingHours.start);
    const end = parseTimeToHour(workingHours.end);
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return null;
    return {
      start: Math.max(0, start - WINDOW_MARGIN_HOURS),
      end: Math.min(HOURS_PER_DAY, end + WINDOW_MARGIN_HOURS),
    };
  }, [workingHours]);

  const scrollTime = workingWindow ? `${workingWindow.start}:00:00` : undefined;

  // Slot height is CSS (FullCalendar has no option for it), so the measured value goes to the
  // stylesheet as a custom property. Measured rather than computed from the viewport because the
  // calendar shares the page with the header, tabs and filter bar, and only the pane knows what
  // is left. Floored at the static 16px so a short window never renders *less* dense than before.
  const [slotHeight, setSlotHeight] = useState<number | null>(null);
  const paneRef = useRef<HTMLDivElement | null>(null);
  const observerRef = useRef<ResizeObserver | null>(null);

  const paneHeightRef = useRef(0);

  const measure = useCallback(() => {
    const el = paneRef.current;
    if (!el || !workingWindow || paneHeightRef.current <= 0) return;
    // The day headers sit inside the pane and do not scroll, so they are not available to slots.
    const header = el.querySelector<HTMLElement>(".fc-col-header");
    const available = paneHeightRef.current - (header?.clientHeight ?? 0);
    const slots = (workingWindow.end - workingWindow.start) * SLOTS_PER_HOUR;
    if (available <= 0) return;
    setSlotHeight(Math.max(MIN_SLOT_PX, Math.floor(available / slots)));
  }, [workingWindow]);

  const attachPane = useCallback((el: HTMLDivElement | null) => {
    paneRef.current = el;
    observerRef.current?.disconnect();
    observerRef.current = null;
    if (!el || typeof ResizeObserver === "undefined") return;
    observerRef.current = new ResizeObserver((entries) => {
      // The entry's own box, not clientHeight: it is what the observer already measured, and it
      // is the height reported before layout is readable back off the element.
      paneHeightRef.current = entries[0]?.contentRect.height ?? el.clientHeight;
      measure();
    });
    observerRef.current.observe(el);
  }, [measure]);

  // The headers only exist once FullCalendar has rendered, and their height changes with the
  // view (a day column header is not a week's), so re-measure on those too — the observer alone
  // sees the pane, which does not resize when the view does.
  useEffect(() => {
    measure();
  }, [measure, active, initialView]);

  useEffect(() => () => observerRef.current?.disconnect(), []);

  // Re-apply the opening scroll once the rows have their fitted height. FullCalendar turns
  // scrollTime into a pixel offset when it mounts the view, against whatever the slots measured
  // then — the 16px fallback. Taller rows leave that offset pointing hours earlier, which is why
  // the view opened on the middle of the night. Scrolling by time again resolves it against the
  // heights now in force.
  useEffect(() => {
    if (!active || !scrollTime) return;
    calendarRef.current?.getApi()?.scrollToTime(scrollTime);
  }, [active, initialView, slotHeight, scrollTime]);

  // Filter state is local and not in the URL: a search query changes on every keystroke, and
  // writing that to the address bar would bury real navigation under typing history.
  const [filter, setFilter] = useState<ScheduleFilter>(DEFAULT_SCHEDULE_FILTER);
  const visibleEvents = useMemo(() => filterCalendarEvents(events, filter), [events, filter]);

  const allEvents = useMemo<EventInput[]>(() => {
    const bgEvents: EventInput[] = (offTimeRanges ?? []).map((r) => ({
      id: `offtime-${r.id}`,
      start: new Date(r.startMs),
      end: new Date(r.endMs),
      display: "background",
    }));
    // Off-time shading is the week's shape, not a request, so the filter never hides it.
    return [...(visibleEvents as EventInput[]), ...bgEvents];
  }, [visibleEvents, offTimeRanges]);

  const handleEventClick = (arg: EventClickArg) => {
    onEventClick(arg.event.id);
  };

  // Move/resize both report the moved event's new bounds. We only forward
  // start/end; the page re-sends the request's existing space resourceId so
  // assignments are never touched.
  const handleEventDrop = (arg: EventDropArg) => {
    if (arg.event.start && arg.event.end) {
      onEventMove(arg.event.id, arg.event.start, arg.event.end);
    } else {
      arg.revert();
    }
  };

  const handleEventResize = (arg: EventResizeDoneArg) => {
    if (arg.event.start && arg.event.end) {
      onEventResize(arg.event.id, arg.event.start, arg.event.end);
    } else {
      arg.revert();
    }
  };

  const handleSelect = (arg: DateSelectArg) => {
    onSlotSelect(arg.start, arg.end);
  };

  const handleDatesSet = (arg: DatesSetArg) => {
    // Ignore FullCalendar's own initial/hidden datesSet — only report while the
    // Calendar tab is showing, so a background calendar never clobbers the anchor
    // the grid is driving.
    if (active) onDatesSet(arg.view.currentStart);
  };

  return (
    <div className="orkyo-calendar flex flex-col h-full">
      {/* Legend on the left, search and filters opposite it. The legend itself is hidden on
          phones — the list (agenda) view tints each row by status, so the 7-item key (which wraps
          to ~3 rows on a narrow screen) is redundant there — but the search stays, because a
          narrow screen is where finding one request by name matters most. */}
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-3 py-2 border-b text-xs text-muted-foreground shrink-0">
        {!isPhone && (
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            {legend.map((item) => (
              <LegendItem key={item.label} className={item.className} label={item.label} />
            ))}
          </div>
        )}
        {showFilterBar && (
          <ScheduleFilterBar
            value={filter}
            onChange={(patch) => setFilter((current) => ({ ...current, ...patch }))}
            matchCount={visibleEvents.length}
            totalCount={events.length}
          />
        )}
      </div>
      <div
        ref={attachPane}
        className="flex-1 min-h-0"
        style={slotHeight ? ({ "--orkyo-slot-height": `${slotHeight}px` } as React.CSSProperties) : undefined}
      >
      <FullCalendar
        ref={calendarRef}
        plugins={plugins}
        locale={locale}
        initialView={initialView}
        initialDate={initialDate}
        headerToolbar={false}
        height="100%"
        expandRows
        allDaySlot={false}
        // Open on the working window rather than FullCalendar's fixed 06:00. Undefined keeps
        // that default for hosts without working hours. scrollTimeReset stays on, so stepping to
        // another week comes back to the working day instead of keeping a scrolled-away position.
        scrollTime={scrollTime}
        nowIndicator
        firstDay={1}
        // Overlapping events partition into side-by-side columns instead of the
        // library default, which stretches each column across half its neighbour and
        // paints the later event on top — at our 16px slot height that buried short
        // events entirely.
        slotEventOverlap={false}
        // Deterministic column order (longest first at equal starts), so a re-render
        // or refetch never shuffles events between columns under the pointer.
        eventOrder="start,-duration,title"
        // Axis time labels share the grid's formatCompactTime so both read identically (24h default).
        slotLabelContent={(arg) => formatCompactTime(arg.date)}
        businessHours={businessHoursConfig}
        editable={editable}
        eventStartEditable={editable}
        eventDurationEditable={editable}
        selectable={editable}
        selectMirror
        dayMaxEvents
        views={{
          timeGridWeek: {
            // "Mon 08" — shares GRID_DAY_HEADER_OPTS with the timeline grid's day label.
            dayHeaderFormat: GRID_DAY_HEADER_OPTS,
          },
          timeGridDay: {
            // Single-column day view: show full context
            dayHeaderFormat: { weekday: 'long', month: 'short', day: 'numeric' },
          },
        }}
        events={allEvents}
        // Native title rather than the Tooltip component: FullCalendar owns these nodes and
        // creates them outside React's tree, and the grid's own bars answer a hover the same
        // way. Set on the element FullCalendar wraps around the content, so the whole block is
        // the hover target — including the list view on phones, which renders its own row.
        eventDidMount={(arg) => {
          if (arg.event.display === "background" || !arg.event.start) return;
          arg.el.title = calendarEventTooltip({
            title: arg.event.title,
            start: arg.event.start,
            end: arg.event.end,
            status: arg.event.extendedProps?.status as RequestStatus | undefined,
            conflictSeverity: (arg.event.extendedProps?.conflictSeverity ?? null) as ConflictSeverity,
          });
        }}
        eventClick={handleEventClick}
        eventDrop={handleEventDrop}
        eventResize={handleEventResize}
        select={handleSelect}
        datesSet={handleDatesSet}
        eventContent={(arg) => {
          // List (agenda) views — phone only — render FullCalendar's native row
          // (time column + colored dot + full, wrapping title). The compact
          // truncated layout below is tuned for narrow grid cells and would clip
          // titles in a full-width list; returning true keeps the default row.
          if (arg.view?.type?.startsWith("list")) return true;
          const severity = arg.event.extendedProps?.conflictSeverity as ConflictSeverity | undefined;
          const presentation = severity ? severityPresentation(severity) : null;
          return (
            <div className="flex items-start gap-1 overflow-hidden h-full px-0.5 min-w-0">
              {presentation && (
                <presentation.icon className={cn("h-3 w-3 flex-shrink-0", presentation.iconClass)} />
              )}
              {/* In a narrow overlap column something must give. The title is what
                  identifies the event, so it keeps a readable floor and ellipsizes;
                  the time label is the one that collapses to nothing (the row's
                  vertical position already encodes it). */}
              {arg.event.start && (
                <span className="text-[10px] tabular-nums opacity-80 leading-4 truncate">
                  {formatCompactTime(arg.event.start)}
                </span>
              )}
              <span className="truncate text-xs font-medium min-w-[4ch]">{arg.event.title}</span>
            </div>
          );
        }}
      />
      </div>
    </div>
  );
}

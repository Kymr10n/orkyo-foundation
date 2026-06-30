import { useMemo } from "react";
import FullCalendar from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/daygrid";
import timeGridPlugin from "@fullcalendar/timegrid";
import interactionPlugin, { type EventResizeDoneArg } from "@fullcalendar/interaction";
import type { DateSelectArg, EventClickArg, EventDropArg, DatesSetArg, EventInput, BusinessHoursInput } from "@fullcalendar/core";
import { USER_LOCALE, formatCompactTime, GRID_DAY_HEADER_OPTS } from "@foundation/src/lib/formatters";
import type { CalendarEvent, CalendarView, ConflictSeverity } from "./request-calendar-events";
import { calendarViewToScale, SEVERITY_SWATCH } from "./request-calendar-events";
import { AlertCircle, AlertTriangle } from "lucide-react";
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
  onEventClick: (requestId: string) => void;
  /** Drag (move) — preserves duration. */
  onEventMove: (requestId: string, start: Date, end: Date) => void;
  onEventResize: (requestId: string, start: Date, end: Date) => void;
  /** Empty-slot selection → schedule chooser. */
  onSlotSelect: (start: Date, end: Date) => void;
  /** Fires on view/range change so the page can keep the store window aligned. */
  onDatesSet: (scale: "day" | "week" | "month", activeStart: Date) => void;
}

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
 * FullCalendar wiring; colours/data come from request-calendar-events.ts and the
 * page's existing hooks. Outlook-style toolbar (prev/next/today + Day/Week/Month)
 * is provided natively so the tab feels familiar; the store window stays in sync
 * via onDatesSet.
 */
export function RequestCalendar({
  events,
  offTimeRanges,
  workingHours,
  editable,
  initialView,
  initialDate,
  onEventClick,
  onEventMove,
  onEventResize,
  onSlotSelect,
  onDatesSet,
}: RequestCalendarProps) {
  const plugins = useMemo(() => [dayGridPlugin, timeGridPlugin, interactionPlugin], []);

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

  const allEvents = useMemo<EventInput[]>(() => {
    const bgEvents: EventInput[] = (offTimeRanges ?? []).map((r) => ({
      id: `offtime-${r.id}`,
      start: new Date(r.startMs),
      end: new Date(r.endMs),
      display: "background",
    }));
    return [...(events as EventInput[]), ...bgEvents];
  }, [events, offTimeRanges]);

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
    onDatesSet(calendarViewToScale(arg.view.type), arg.view.currentStart);
  };

  return (
    <div className="orkyo-calendar flex flex-col h-full">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 border-b text-xs text-muted-foreground shrink-0">
        <LegendItem className="bg-blue-500/15 dark:bg-blue-500/25 border-blue-500/40" label="New" />
        <LegendItem className="bg-amber-500/15 dark:bg-amber-500/25 border-amber-500/40" label="In Progress" />
        <LegendItem className="bg-emerald-500/15 dark:bg-emerald-500/25 border-emerald-500/40" label="Done" />
        <LegendItem className="bg-slate-500/15 dark:bg-slate-500/25 border-slate-400/40" label="Deferred" />
        <LegendItem className="bg-muted border-muted-foreground/30" label="Cancelled" />
        <LegendItem className={SEVERITY_SWATCH.error} label="Conflicts" />
        <LegendItem className={SEVERITY_SWATCH.warning} label="Warnings" />
      </div>
      <div className="flex-1 min-h-0">
      <FullCalendar
        plugins={plugins}
        locale={locale}
        initialView={initialView}
        initialDate={initialDate}
        headerToolbar={{
          left: "prev,next today",
          center: "title",
          right: "timeGridDay,timeGridWeek,dayGridMonth",
        }}
        buttonText={{ today: "Today", day: "Day", week: "Week", month: "Month" }}
        height="100%"
        expandRows
        allDaySlot={false}
        nowIndicator
        firstDay={1}
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
        eventClick={handleEventClick}
        eventDrop={handleEventDrop}
        eventResize={handleEventResize}
        select={handleSelect}
        datesSet={handleDatesSet}
        eventContent={(arg) => {
          const severity = arg.event.extendedProps?.conflictSeverity as ConflictSeverity | undefined;
          return (
            <div className="flex items-start gap-1 overflow-hidden h-full px-0.5 min-w-0">
              {severity === 'error'   && <AlertCircle   className="h-3 w-3 flex-shrink-0 text-red-600" />}
              {severity === 'warning' && <AlertTriangle className="h-3 w-3 flex-shrink-0 text-amber-600" />}
              {arg.event.start && (
                <span className="text-[10px] flex-shrink-0 tabular-nums opacity-80 leading-4">
                  {formatCompactTime(arg.event.start)}
                </span>
              )}
              <span className="truncate text-xs font-medium">{arg.event.title}</span>
            </div>
          );
        }}
      />
      </div>
    </div>
  );
}

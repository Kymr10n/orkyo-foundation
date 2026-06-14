import { useMemo } from "react";
import FullCalendar from "@fullcalendar/react";
import dayGridPlugin from "@fullcalendar/daygrid";
import timeGridPlugin from "@fullcalendar/timegrid";
import interactionPlugin, { type EventResizeDoneArg } from "@fullcalendar/interaction";
import type { DateSelectArg, EventClickArg, EventDropArg, DatesSetArg, EventInput, BusinessHoursInput } from "@fullcalendar/core";
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
        <LegendItem className="bg-blue-100 dark:bg-blue-950 border-blue-200 dark:border-blue-800" label="Planned" />
        <LegendItem className="bg-amber-100 dark:bg-amber-950 border-amber-200 dark:border-amber-800" label="In Progress" />
        <LegendItem className="bg-emerald-100 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800" label="Done" />
        <LegendItem className="bg-muted border-muted-foreground/30" label="Cancelled" />
        <LegendItem className={SEVERITY_SWATCH.error} label="Conflicts" />
        <LegendItem className={SEVERITY_SWATCH.warning} label="Warnings" />
      </div>
      <div className="flex-1 min-h-0">
      <FullCalendar
        plugins={plugins}
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
        nowIndicator
        firstDay={1}
        businessHours={businessHoursConfig}
        editable={editable}
        eventStartEditable={editable}
        eventDurationEditable={editable}
        selectable={editable}
        selectMirror
        dayMaxEvents
        views={{
          timeGridWeek: {
            // "Mon 08" — matches the timeline grid's EEE dd label
            dayHeaderFormat: { weekday: 'short', day: '2-digit' },
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
            <div className="flex flex-col overflow-hidden h-full px-0.5 py-px gap-0">
              {arg.timeText && (
                <div className="text-[10px] leading-tight truncate opacity-80">{arg.timeText}</div>
              )}
              <div className="flex items-center gap-1 min-w-0">
                {severity === 'error'   && <AlertCircle   className="h-3 w-3 flex-shrink-0 text-red-600" />}
                {severity === 'warning' && <AlertTriangle className="h-3 w-3 flex-shrink-0 text-amber-600" />}
                <span className="truncate text-xs font-medium">{arg.event.title}</span>
              </div>
            </div>
          );
        }}
      />
      </div>
    </div>
  );
}

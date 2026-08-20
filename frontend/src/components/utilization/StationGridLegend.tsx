import {
  OFFTIME_TINT_CLASS,
  PROBLEM_HATCH_CLASS,
  STATUS_BORDER_CLASS,
  STATUS_CELL_CLASS,
} from './schedule-colors';

/**
 * The key for the stations grid.
 *
 * Deliberately not the calendar's key. A calendar block is coloured by the request's *status*; a
 * grid bar is coloured by what it does to the station — assigned, or overbooked — and the columns
 * behind it carry off-time. Showing the calendar's five statuses here would name colours this grid
 * never paints.
 *
 * Swatches read from the same maps `ScheduledRequestOverlay` and the row backgrounds use, so the
 * key cannot drift from what is on screen.
 */
export function StationGridLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
      <LegendItem
        className={`${STATUS_CELL_CLASS.assigned} ${STATUS_BORDER_CLASS.assigned}`}
        label="Assigned"
      />
      <LegendItem
        // Overbooked carries the hatch as its non-colour cue (WCAG 1.4.1), so the swatch does too.
        className={`${STATUS_CELL_CLASS.overbooked} ${STATUS_BORDER_CLASS.overbooked} ${PROBLEM_HATCH_CLASS}`}
        label="Overbooked"
      />
      <LegendItem
        className={`${OFFTIME_TINT_CLASS} ${PROBLEM_HATCH_CLASS} border-muted-foreground/30`}
        label="Off-time"
      />
    </div>
  );
}

function LegendItem({ className, label }: { className: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={`inline-block h-3 w-3 rounded-sm border ${className}`} aria-hidden />
      {label}
    </span>
  );
}

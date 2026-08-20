import { type BucketStatus, STATUS_BORDER_CLASS, STATUS_CELL_CLASS, STATUS_PATTERN_CLASS } from './schedule-colors';

function LegendDot({ status, label, title }: { status: BucketStatus; label: string; title?: string }) {
  return (
    <span className="inline-flex items-center gap-1.5" title={title}>
      <span
        className={`inline-block h-2.5 w-4 rounded-sm border ${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]} ${STATUS_PATTERN_CLASS[status]}`}
      />
      {label}
    </span>
  );
}

/**
 * The key for the asset grids.
 *
 * Not the calendar's key and not the stations grid's: an asset row is a utilization meter, so its
 * colours name how much of the period is spoken for. Swatches come from the same maps the row
 * segments use, so the key cannot drift from what is on screen.
 */
export function AssetGridLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
      <LegendDot status="available" label="Available" />
      <LegendDot
        status="partial"
        label="Booked"
        title="Booked % = share of this period the resource is allocated (time-weighted)."
      />
      <LegendDot status="assigned" label="Assigned" />
      <LegendDot
        status="overbooked"
        label="Overbooked"
        title="Allocated beyond capacity (>100%) in this period."
      />
      <LegendDot status="non-working" label="Off" />
    </div>
  );
}

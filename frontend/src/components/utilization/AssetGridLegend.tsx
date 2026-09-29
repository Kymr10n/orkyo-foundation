import { Legend, type LegendItem } from '@foundation/src/components/ui/Legend';
import { STATUS_BORDER_CLASS, STATUS_CELL_CLASS, STATUS_PATTERN_CLASS } from './schedule-colors';
import type { BucketStatus } from '@foundation/src/domain/scheduling/types';

function item(status: BucketStatus, label: string, title?: string): LegendItem {
  return {
    className: `${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]} ${STATUS_PATTERN_CLASS[status]}`,
    label,
    title,
  };
}

const ASSET_LEGEND: readonly LegendItem[] = [
  item('available', 'Available'),
  item('partial', 'Booked', 'Booked % = share of this period the resource is allocated (time-weighted).'),
  item('assigned', 'Assigned'),
  item('overbooked', 'Overbooked', 'Allocated beyond capacity (>100%) in this period.'),
  item('non-working', 'Off'),
];

/**
 * The key for the asset grids.
 *
 * Not the calendar's key and not the stations grid's: an asset row is a utilization meter, so its
 * colours name how much of the period is spoken for. Swatches come from the same maps the row
 * segments use, so the key cannot drift from what is on screen.
 */
export function AssetGridLegend() {
  return <Legend items={ASSET_LEGEND} />;
}

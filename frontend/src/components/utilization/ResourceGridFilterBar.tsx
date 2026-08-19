import { useMemo } from 'react';
import { Search, X } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { CheckableFilterMenu } from '@foundation/src/components/ui/CheckableFilterMenu';
import {
  EMPTY_RESOURCE_GRID_FILTER,
  UTILIZATION_FILTER_ORDER,
  type ResourceGridFilter,
} from './resource-grid-filter';
import { STATUS_BORDER_CLASS, STATUS_CELL_CLASS, STATUS_PATTERN_CLASS } from './schedule-colors';
import type { BucketStatus } from './schedule-colors';

interface ResourceGridFilterBarProps {
  value: ResourceGridFilter;
  onChange: (next: ResourceGridFilter) => void;
}

/** How the filter menu names each utilization state — the legend's wording, exactly. */
const UTILIZATION_LABELS: Record<BucketStatus, string> = {
  available: 'Available',
  partial: 'Booked',
  assigned: 'Assigned',
  overbooked: 'Overbooked',
  'non-working': 'Off',
};

/**
 * Search and filters for the Assets tab, sitting opposite its legend.
 *
 * One bar for the whole tab rather than one per stacked type grid: the search says "assets"
 * because it narrows every asset on screen, which a per-grid box could not honestly claim.
 */
export function ResourceGridFilterBar({ value, onChange }: ResourceGridFilterBarProps) {
  const stateItems = useMemo(
    () =>
      UTILIZATION_FILTER_ORDER.map((status) => ({
        value: status,
        label: UTILIZATION_LABELS[status],
        swatchClassName: `${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]} ${STATUS_PATTERN_CLASS[status]}`,
      })),
    [],
  );

  const isFiltered =
    value.query.trim().length > 0 || value.states.length !== UTILIZATION_FILTER_ORDER.length;

  return (
    <div className="flex flex-wrap items-center justify-end gap-2">
      <div className="relative">
        <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          type="search"
          placeholder="Search assets…"
          aria-label="Search assets"
          value={value.query}
          onChange={(e) => onChange({ ...value, query: e.target.value })}
          className="h-8 w-40 pl-7 text-sm sm:w-56"
        />
      </div>

      <CheckableFilterMenu
        items={stateItems}
        selected={value.states}
        onChange={(states) => onChange({ ...value, states: states as BucketStatus[] })}
        allLabel="All states"
        noun="states"
        ariaLabel="Filter by utilization"
      />

      {isFiltered && (
        <Button variant="ghost" size="sm" onClick={() => onChange(EMPTY_RESOURCE_GRID_FILTER)}>
          <X className="mr-1 h-3.5 w-3.5" />
          Clear
        </Button>
      )}
    </div>
  );
}

import { useMemo } from 'react';
import { Search, X } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { CheckableFilterMenu } from '@foundation/src/components/ui/CheckableFilterMenu';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { formatStatusLabel } from '@foundation/src/lib/utils/utils';
import type { RequestStatus } from '@foundation/src/types/requests';
import { SEVERITY_SWATCH, STATUS_SWATCH } from './request-calendar-events';
import { ISSUE_FILTER, ISSUE_FILTER_ORDER, type ScheduleFilter, type IssueFilter } from './schedule-filter';

interface ScheduleFilterBarProps {
  value: ScheduleFilter;
  onChange: (patch: Partial<ScheduleFilter>) => void;
  /** How many events survive the filter, for the cleared-everything hint. */
  matchCount: number;
  totalCount: number;
}

const ISSUE_LABELS: Record<IssueFilter, string> = {
  [ISSUE_FILTER.ERROR]: 'Conflicts',
  [ISSUE_FILTER.WARNING]: 'Warnings',
  [ISSUE_FILTER.NONE]: 'No issues',
};

const ISSUE_SWATCHES: Partial<Record<IssueFilter, string>> = {
  [ISSUE_FILTER.ERROR]: SEVERITY_SWATCH.error,
  [ISSUE_FILTER.WARNING]: SEVERITY_SWATCH.warning,
};

/**
 * Search and filters for a schedule surface, sitting opposite its legend. Used by the calendar and
 * by the stations grid, which show the same requests two ways and so filter by the same three
 * things: name, request status, and whether the request has conflicts.
 *
 * Status swatches come from the same map the calendar events and its legend use. The stations grid
 * paints occupancy rather than status, so its own legend differs — but what it *filters* by is the
 * request, and that is the same everywhere.
 */
export function ScheduleFilterBar({ value, onChange, matchCount, totalCount }: ScheduleFilterBarProps) {
  const statusItems = useMemo(
    () =>
      REQUEST_STATUS_ORDER.map((status) => ({
        value: status,
        label: formatStatusLabel(status),
        swatchClassName: STATUS_SWATCH[status],
      })),
    [],
  );

  const issueItems = useMemo(
    () =>
      ISSUE_FILTER_ORDER.map((issue) => ({
        value: issue,
        label: ISSUE_LABELS[issue],
        swatchClassName: ISSUE_SWATCHES[issue],
      })),
    [],
  );

  const isFiltered =
    value.query.trim().length > 0 ||
    value.statuses.length !== REQUEST_STATUS_ORDER.length ||
    value.issues.length !== ISSUE_FILTER_ORDER.length;

  return (
    <div className="flex flex-wrap items-center justify-end gap-2">
      {/* Hidden until something is filtered out — a count that never changes is noise, and a
          zero-result calendar otherwise looks like a week with nothing scheduled. */}
      {isFiltered && (
        <span className="text-xs text-muted-foreground" role="status">
          {matchCount} of {totalCount}
        </span>
      )}

      <div className="relative">
        <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          type="search"
          value={value.query}
          onChange={(e) => onChange({ query: e.target.value })}
          placeholder="Search requests…"
          aria-label="Search requests"
          className="h-8 w-40 pl-7 text-sm sm:w-56"
        />
      </div>

      <CheckableFilterMenu
        items={statusItems}
        selected={value.statuses}
        onChange={(statuses) => onChange({ statuses: statuses as RequestStatus[] })}
        allLabel="All statuses"
        noun="statuses"
        ariaLabel="Filter by status"
      />

      <CheckableFilterMenu
        items={issueItems}
        selected={value.issues}
        onChange={(issues) => onChange({ issues: issues as IssueFilter[] })}
        allLabel="All issues"
        noun="issues"
        ariaLabel="Filter by issue"
      />

      {isFiltered && (
        <Button
          variant="ghost"
          size="sm"
          onClick={() =>
            onChange({
              query: '',
              statuses: REQUEST_STATUS_ORDER,
              issues: ISSUE_FILTER_ORDER,
            })
          }
        >
          <X className="mr-1 h-3.5 w-3.5" />
          Clear
        </Button>
      )}
    </div>
  );
}

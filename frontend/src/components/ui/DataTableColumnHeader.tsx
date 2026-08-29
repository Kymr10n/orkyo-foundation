import { useState, type ReactNode } from 'react';
import type { RowData } from '@tanstack/table-core';
import type { Column } from '@foundation/src/lib/table/features';
import { ArrowDown, ArrowUp, Check, ChevronsUpDown } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@foundation/src/components/ui/popover';
import { Separator } from '@foundation/src/components/ui/separator';
import { flattenFacets } from '@foundation/src/lib/table/filter-fns';
import type { ColumnFilterMeta } from '@foundation/src/lib/table/column-meta';
import { cn } from '@foundation/src/lib/utils';

/**
 * The one interactive column header. Rendered by OrkyoDataTable for every column that can
 * sort or declares meta.filter — call sites keep their plain `header: 'Name'` strings and
 * this wraps whatever they render. Clicking opens a compact menu: sort, then a filter suited
 * to the column's declared type. One component so every list in the product behaves, looks
 * and reads identically.
 */

/** Sort wording per type: "A → Z" reads wrong on a date column and worse on a number one. */
const SORT_LABELS: Record<ColumnFilterMeta['type'] | 'default', [string, string]> = {
  text: ['Sort A → Z', 'Sort Z → A'],
  enum: ['Sort A → Z', 'Sort Z → A'],
  date: ['Oldest first', 'Newest first'],
  number: ['Low → High', 'High → Low'],
  default: ['Sort A → Z', 'Sort Z → A'],
};

interface DataTableColumnHeaderProps<TData extends RowData> {
  column: Column<TData, unknown>;
  /** The already-rendered header content from the columnDef — string or element. */
  title: ReactNode;
  /** Plain-text name for aria labels, resolved by the table from meta.label or the header string. */
  label: string;
  /**
   * Server-paged tables hold one window of the rows, so facet counts taken from them would
   * describe the page rather than the data — the table sets this to drop the counts instead
   * of showing numbers that are quietly wrong.
   */
  facetsUnavailable?: boolean;
}

export function DataTableColumnHeader<TData extends RowData>({
  column,
  title,
  label,
  facetsUnavailable,
}: DataTableColumnHeaderProps<TData>) {
  const [open, setOpen] = useState(false);
  const meta = column.columnDef.meta?.filter;
  const canSort = column.getCanSort();
  const sorted = column.getIsSorted();
  const isFiltered = column.getIsFiltered();
  const [sortAscLabel, sortDescLabel] = SORT_LABELS[meta?.type ?? 'default'];

  // Selected-facet count for the pill; text/range filters show a dot-sized pill without one.
  const filterValue = column.getFilterValue();
  const facetCount = Array.isArray(filterValue) && meta?.type === 'enum' ? filterValue.length : null;

  const setSort = (desc: boolean) => {
    // Clicking the active direction clears it — the menu is also the off switch.
    if (sorted === (desc ? 'desc' : 'asc')) column.clearSorting();
    else column.toggleSorting(desc, false);
    // Sorting is a one-shot choice, so the menu closes; filters stay open because
    // narrowing usually takes several adjustments.
    setOpen(false);
  };

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-label={`${label} — sort and filter`}
          // `whitespace-normal` overrides the button default: a header that cannot wrap sets the
          // table's minimum width, so one long label like "Downtime (h)" forces the whole grid
          // into horizontal scroll inside a narrow container. With room the label still sits on
          // one line — this only lets it wrap when the alternative is a scrollbar.
          className="group -ml-2 h-auto min-h-7 gap-1.5 whitespace-normal px-2 py-1 text-left font-medium text-muted-foreground data-[state=open]:bg-accent data-[state=open]:text-accent-foreground"
        >
          {title}
          {sorted === 'asc' ? (
            <ArrowUp className="h-3.5 w-3.5 shrink-0" />
          ) : sorted === 'desc' ? (
            <ArrowDown className="h-3.5 w-3.5 shrink-0" />
          ) : canSort ? (
            <ChevronsUpDown className="h-3.5 w-3.5 shrink-0 opacity-0 group-hover:opacity-50" />
          ) : null}
          {isFiltered && (
            <span className="flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-bold text-primary-foreground">
              {facetCount ?? '•'}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-56 p-0" align="start" aria-label={`${label} — sort and filter`}>
        {canSort && (
          <div className="py-1">
            <SortOption
              label={sortAscLabel}
              active={sorted === 'asc'}
              onClick={() => setSort(false)}
            />
            <SortOption
              label={sortDescLabel}
              active={sorted === 'desc'}
              onClick={() => setSort(true)}
            />
          </div>
        )}
        {canSort && meta && <Separator />}
        {meta && (
          <div className="p-2">
            {meta.type === 'text' && <TextFilter column={column} label={label} />}
            {meta.type === 'enum' && (
              <EnumFilter
                column={column}
                meta={meta}
                label={label}
                facetsUnavailable={facetsUnavailable}
              />
            )}
            {(meta.type === 'date' || meta.type === 'number') && (
              <RangeFilter column={column} kind={meta.type} label={label} />
            )}
            {isFiltered && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="mt-2 h-7 w-full justify-start px-2 text-muted-foreground"
                onClick={() => column.setFilterValue(undefined)}
              >
                Clear filter
              </Button>
            )}
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}

/** A combobox-style option row: Check toggled by opacity, bg-accent/50 when active. */
function SortOption({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm',
        'hover:bg-accent hover:text-accent-foreground focus:bg-accent focus:outline-hidden',
        active && 'bg-accent/50',
      )}
    >
      <Check className={cn('h-4 w-4 shrink-0', active ? 'opacity-100' : 'opacity-0')} />
      {label}
    </button>
  );
}

function TextFilter<TData extends RowData>({ column, label }: { column: Column<TData, unknown>; label: string }) {
  return (
    <Input
      value={(column.getFilterValue() as string) ?? ''}
      onChange={(e) => column.setFilterValue(e.target.value || undefined)}
      placeholder="Contains…"
      aria-label={`Filter ${label}`}
      className="h-8"
    />
  );
}

function EnumFilter<TData extends RowData>({
  column,
  meta,
  label,
  facetsUnavailable,
}: {
  column: Column<TData, unknown>;
  meta: Extract<ColumnFilterMeta, { type: 'enum' }>;
  label: string;
  facetsUnavailable?: boolean;
}) {
  // Counts come from the faceted model, which reflects the rows the *other* filters leave —
  // so counts narrow as you filter elsewhere, which is what makes them useful.
  const rawFacets = facetsUnavailable
    ? new Map<unknown, number>()
    : column.getFacetedUniqueValues();
  const faceted = meta.isArray ? flattenFacets(rawFacets) : rawFacets;
  const options = [...faceted.entries()]
    .map(([value, count]) => {
      const raw = String(value);
      return { raw, label: meta.getLabel?.(raw) ?? raw, count };
    })
    .sort((a, b) => a.label.localeCompare(b.label));

  const selected = (column.getFilterValue() as string[] | undefined) ?? [];

  const toggle = (raw: string, checked: boolean) => {
    const next = checked ? [...selected, raw] : selected.filter((v) => v !== raw);
    // autoRemove on the filterFn treats [] as "no filter"; undefined keeps the state clean.
    column.setFilterValue(next.length > 0 ? next : undefined);
  };

  return (
    <div className="max-h-60 space-y-1 overflow-y-auto" role="group" aria-label={`Filter ${label}`}>
      {options.map((o) => (
        <div key={o.raw} className="flex items-center gap-2 px-1 py-0.5">
          <Checkbox
            id={`${column.id}-facet-${o.raw}`}
            checked={selected.includes(o.raw)}
            onCheckedChange={(checked) => toggle(o.raw, checked === true)}
          />
          <Label
            htmlFor={`${column.id}-facet-${o.raw}`}
            className="flex-1 cursor-pointer truncate text-sm font-normal"
          >
            {o.label}
          </Label>
          <span className="text-xs text-muted-foreground">{o.count}</span>
        </div>
      ))}
    </div>
  );
}

function RangeFilter<TData extends RowData>({
  column,
  kind,
  label,
}: {
  column: Column<TData, unknown>;
  kind: 'date' | 'number';
  label: string;
}) {
  const value = (column.getFilterValue() as [string?, string?] | undefined) ?? [undefined, undefined];

  const setBound = (index: 0 | 1, raw: string) => {
    const next: [string?, string?] = [...value] as [string?, string?];
    next[index] = raw || undefined;
    column.setFilterValue(next[0] === undefined && next[1] === undefined ? undefined : next);
  };

  const inputType = kind === 'date' ? 'date' : 'number';

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <Label htmlFor={`${column.id}-from`} className="w-10 shrink-0 text-xs text-muted-foreground">
          {kind === 'date' ? 'From' : 'Min'}
        </Label>
        <Input
          id={`${column.id}-from`}
          type={inputType}
          value={value[0] ?? ''}
          onChange={(e) => setBound(0, e.target.value)}
          aria-label={`Filter ${label} from`}
          className="h-8"
        />
      </div>
      <div className="flex items-center gap-2">
        <Label htmlFor={`${column.id}-to`} className="w-10 shrink-0 text-xs text-muted-foreground">
          {kind === 'date' ? 'To' : 'Max'}
        </Label>
        <Input
          id={`${column.id}-to`}
          type={inputType}
          value={value[1] ?? ''}
          onChange={(e) => setBound(1, e.target.value)}
          aria-label={`Filter ${label} to`}
          className="h-8"
        />
      </div>
    </div>
  );
}

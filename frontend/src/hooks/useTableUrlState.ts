import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import type {
  ColumnFiltersState,
  OnChangeFn,
  RowData,
  SortingState,
  Updater,
} from '@tanstack/react-table';
import type { ColumnDef } from '@foundation/src/lib/table/features';
import type { ColumnFilterMeta } from '@foundation/src/lib/table/column-meta';
import { useDebouncedCallback } from '@foundation/src/hooks/useDebouncedCallback';

/**
 * Persists a table's sort + column-filter state in the URL, so a filtered view is
 * bookmarkable, shareable, and survives Back. Spread the result onto OrkyoDataTable.
 *
 * Follows useTabParam's contract: the URL is the single source of truth, writes use
 * `{ replace: true }` so filtering never stacks history, and unrelated params (`?tab=`,
 * `?edit=`) are preserved. Every param carries the table's `urlKey` prefix so two tables on
 * one page (UserSettings) cannot collide.
 *
 * Encoding: `{key}_s=name.desc` for sort; filters as `{key}_f_{columnId}` with a shape decided
 * by the column's declared meta type — enum `a~b`, text verbatim, range `lo..hi` (open ends
 * allowed). Decoding is meta-driven, not syntax-sniffed, so a text filter containing ".." can
 * never be misread as a range. Anything invalid — unknown column, bad direction, a filter on a
 * column that declares none — is ignored and dropped on the next write.
 */
export function useTableUrlState<TData extends RowData>(
  urlKey: string,
  columns: ColumnDef<TData>[],
): {
  sorting: SortingState;
  onSortingChange: OnChangeFn<SortingState>;
  columnFilters: ColumnFiltersState;
  onColumnFiltersChange: OnChangeFn<ColumnFiltersState>;
} {
  const [searchParams, setSearchParams] = useSearchParams();

  // Column arrays are rebuilt every render at several call sites, so decode must not key on
  // the array identity. The meta index is rebuilt from ids+types only when those change.
  const metaByColumn = useMemoStableMeta(columns);

  const sortParam = `${urlKey}_s`;
  const filterPrefix = `${urlKey}_f_`;

  const decoded = useMemo(() => {
    const sorting: SortingState = [];
    const raw = searchParams.get(sortParam);
    if (raw) {
      // Split on the LAST dot: column ids may themselves contain dots.
      const at = raw.lastIndexOf('.');
      const id = raw.slice(0, at);
      const dir = raw.slice(at + 1);
      if ((dir === 'asc' || dir === 'desc') && metaByColumn.has(id)) {
        sorting.push({ id, desc: dir === 'desc' });
      }
    }

    const columnFilters: ColumnFiltersState = [];
    for (const [param, value] of searchParams.entries()) {
      if (!param.startsWith(filterPrefix) || value === '') continue;
      const columnId = param.slice(filterPrefix.length);
      const meta = metaByColumn.get(columnId);
      if (!meta?.filter) continue;

      const parsed = decodeFilterValue(meta.filter, value);
      if (parsed !== undefined) columnFilters.push({ id: columnId, value: parsed });
    }

    return { sorting, columnFilters };
  }, [searchParams, metaByColumn, sortParam, filterPrefix]);

  // Local echo for text filters: the URL write is debounced so typing doesn't churn history
  // state per keystroke, but the input must reflect keystrokes immediately.
  const [echo, setEcho] = useState<ColumnFiltersState | null>(null);

  const writeToUrl = useCallback(
    (sorting: SortingState, filters: ColumnFiltersState) => {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          // Rewrite this table's params wholesale; every other param passes through.
          next.delete(sortParam);
          for (const key of [...next.keys()]) {
            if (key.startsWith(filterPrefix)) next.delete(key);
          }

          const s = sorting[0];
          if (s) next.set(sortParam, `${s.id}.${s.desc ? 'desc' : 'asc'}`);

          for (const f of filters) {
            const meta = metaByColumn.get(f.id);
            if (!meta?.filter) continue;
            const encoded = encodeFilterValue(meta.filter, f.value);
            if (encoded !== undefined) next.set(`${filterPrefix}${f.id}`, encoded);
          }
          return next;
        },
        { replace: true },
      );
      setEcho(null);
    },
    [setSearchParams, sortParam, filterPrefix, metaByColumn],
  );

  const writeDebounced = useDebouncedCallback(writeToUrl, 300);

  const columnFilters = echo ?? decoded.columnFilters;

  const onColumnFiltersChange: OnChangeFn<ColumnFiltersState> = useCallback(
    (updater: Updater<ColumnFiltersState>) => {
      const current = echo ?? decoded.columnFilters;
      const next = typeof updater === 'function' ? updater(current) : updater;
      // Only text filters arrive per keystroke; everything else commits immediately so the
      // URL never lags a click.
      const textChanged = next.some((f) => {
        const meta = metaByColumn.get(f.id);
        return meta?.filter?.type === 'text' && typeof f.value === 'string';
      });
      if (textChanged) {
        setEcho(next);
        writeDebounced(decoded.sorting, next);
      } else {
        writeDebounced.cancel();
        writeToUrl(decoded.sorting, next);
      }
    },
    [decoded, echo, metaByColumn, writeDebounced, writeToUrl],
  );

  const onSortingChange: OnChangeFn<SortingState> = useCallback(
    (updater: Updater<SortingState>) => {
      const next = typeof updater === 'function' ? updater(decoded.sorting) : updater;
      writeDebounced.cancel();
      writeToUrl(next, echo ?? decoded.columnFilters);
    },
    [decoded, echo, writeDebounced, writeToUrl],
  );

  return { sorting: decoded.sorting, onSortingChange, columnFilters, onColumnFiltersChange };
}

// ── Encoding ──────────────────────────────────────────────────────────────────

const ENUM_SEPARATOR = '~';
const RANGE_SEPARATOR = '..';

function encodeFilterValue(meta: ColumnFilterMeta, value: unknown): string | undefined {
  switch (meta.type) {
    case 'text':
      return typeof value === 'string' && value !== '' ? value : undefined;
    case 'enum':
      return Array.isArray(value) && value.length > 0 ? value.join(ENUM_SEPARATOR) : undefined;
    case 'date':
    case 'number': {
      if (!Array.isArray(value)) return undefined;
      const [lo, hi] = value as [unknown?, unknown?];
      // Bounds come from the header's date/number inputs, so anything else is not encodable.
      const bound = (v: unknown) => (typeof v === 'string' || typeof v === 'number' ? String(v) : '');
      const loStr = bound(lo);
      const hiStr = bound(hi);
      if (loStr === '' && hiStr === '') return undefined;
      return `${loStr}${RANGE_SEPARATOR}${hiStr}`;
    }
  }
}

function decodeFilterValue(meta: ColumnFilterMeta, raw: string): unknown {
  switch (meta.type) {
    case 'text':
      return raw;
    case 'enum':
      return raw.split(ENUM_SEPARATOR).filter((v) => v !== '');
    case 'date':
    case 'number': {
      const at = raw.indexOf(RANGE_SEPARATOR);
      if (at === -1) return undefined; // not range-shaped — someone hand-edited the URL
      const lo = raw.slice(0, at);
      const hi = raw.slice(at + RANGE_SEPARATOR.length);
      if (lo === '' && hi === '') return undefined;
      if (meta.type === 'number' && ((lo && Number.isNaN(Number(lo))) || (hi && Number.isNaN(Number(hi))))) {
        return undefined;
      }
      return [lo || undefined, hi || undefined];
    }
  }
}

/** Map of column id → meta, stable across re-created column arrays with identical contents. */
function useMemoStableMeta<TData extends RowData>(columns: ColumnDef<TData>[]) {
  const signature = columns
    .map((c) => {
      const id = columnId(c);
      const f = c.meta?.filter;
      return `${id}:${f ? f.type : ''}`;
    })
    .join('|');

  // Keyed on the content signature, not on `columns`: call sites rebuild their columns array
  // every render, so depending on it would rebuild the map every render and defeat the point.
  // Reading `columns` inside is safe precisely because `signature` covers everything read.
  return useMemo(() => {
    const map = new Map<string, { filter?: ColumnFilterMeta }>();
    for (const c of columns) {
      const id = columnId(c);
      if (id) map.set(id, { filter: c.meta?.filter });
    }
    return map;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signature]);
}

function columnId<TData extends RowData>(c: ColumnDef<TData>): string {
  return c.id ?? ('accessorKey' in c && typeof c.accessorKey === 'string' ? c.accessorKey : '');
}

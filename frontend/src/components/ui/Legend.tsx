import { cn } from "@foundation/src/lib/utils";

export interface LegendItem {
  /** Swatch classes (fill, border, pattern) — read them from the map the surface paints with. */
  className: string;
  label: string;
  /** Optional hover text explaining the entry. */
  title?: string;
}

/**
 * A colour key: a row of swatches, each followed by its label. Every schedule surface
 * (calendar, stations grid, asset grids, floorplan) renders its key through this, so the
 * swatch size and spacing are the same everywhere.
 */
export function Legend({ items, className }: { items: readonly LegendItem[]; className?: string }) {
  return (
    <div className={cn("flex flex-wrap items-center gap-x-4 gap-y-1", className)}>
      {items.map((item) => (
        <span key={item.label} className="inline-flex items-center gap-1.5" title={item.title}>
          <span className={cn("inline-block h-2.5 w-4 rounded-sm border", item.className)} aria-hidden />
          {item.label}
        </span>
      ))}
    </div>
  );
}

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import type { InsightsBucket } from "@foundation/src/lib/api/insights-api";

/**
 * Period presets — a minimal date-range selector (this is a dashboard, not a report builder).
 * Orkyo is a forward-planning scheduler, so presets span history *and* planned work rather than
 * looking only backward like typical analytics — otherwise the upcoming schedule is invisible.
 */
export type RangePreset = "window" | "6m" | "90d" | "next12m";

interface InsightsFiltersProps {
  range: RangePreset;
  onRangeChange: (range: RangePreset) => void;
  bucket: InsightsBucket;
  onBucketChange: (bucket: InsightsBucket) => void;
}

const RANGE_LABELS: Record<RangePreset, string> = {
  window: "Last 6 / next 12 mo",
  "6m": "Last 6 months",
  "90d": "Next 90 days",
  next12m: "Next 12 months",
};

const BUCKET_LABELS: Record<InsightsBucket, string> = {
  week: "Weekly",
  month: "Monthly",
  quarter: "Quarterly",
  year: "Yearly",
};

export function InsightsFilters({ range, onRangeChange, bucket, onBucketChange }: InsightsFiltersProps) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Select value={range} onValueChange={(v) => onRangeChange(v as RangePreset)}>
        <SelectTrigger className="w-[160px]" aria-label="Date range">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {(Object.keys(RANGE_LABELS) as RangePreset[]).map((r) => (
            <SelectItem key={r} value={r}>{RANGE_LABELS[r]}</SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select value={bucket} onValueChange={(v) => onBucketChange(v as InsightsBucket)}>
        <SelectTrigger className="w-[140px]" aria-label="Bucket">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {(Object.keys(BUCKET_LABELS) as InsightsBucket[]).map((b) => (
            <SelectItem key={b} value={b}>{BUCKET_LABELS[b]}</SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

/**
 * Resolves a preset to a concrete [from, to) window anchored at "now". Presets can extend into the
 * future because scheduled work lives ahead of today; the default "window" shows recent history
 * alongside the planning horizon.
 */
export function resolveRange(range: RangePreset): { from: Date; to: Date } {
  // Anchor to the start of the day, not the exact instant: the window only needs day precision, and
  // a stable [from, to) lets the React-Query and server insights caches actually hit across loads
  // (millisecond-precision anchors produced a unique cache key every load → 0% hits) and stops the
  // KPI numbers jittering between refreshes.
  const now = new Date();
  now.setHours(0, 0, 0, 0);
  const from = new Date(now);
  const to = new Date(now);
  switch (range) {
    case "window": from.setMonth(from.getMonth() - 6); to.setMonth(to.getMonth() + 12); break;
    case "6m": from.setMonth(from.getMonth() - 6); break;
    case "90d": to.setDate(to.getDate() + 90); break;
    case "next12m": to.setMonth(to.getMonth() + 12); break;
  }
  return { from, to };
}

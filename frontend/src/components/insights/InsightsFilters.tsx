import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import type { InsightsBucket } from "@foundation/src/lib/api/insights-api";

/** Period presets — a minimal date-range selector (this is a dashboard, not a report builder). */
export type RangePreset = "30d" | "90d" | "12m" | "ytd";

interface InsightsFiltersProps {
  range: RangePreset;
  onRangeChange: (range: RangePreset) => void;
  bucket: InsightsBucket;
  onBucketChange: (bucket: InsightsBucket) => void;
}

const RANGE_LABELS: Record<RangePreset, string> = {
  "30d": "Last 30 days",
  "90d": "Last 90 days",
  "12m": "Last 12 months",
  ytd: "This year",
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

/** Resolves a preset to a concrete [from, to) window anchored at "now". */
export function resolveRange(range: RangePreset): { from: Date; to: Date } {
  const to = new Date();
  const from = new Date(to);
  switch (range) {
    case "30d": from.setDate(from.getDate() - 30); break;
    case "90d": from.setDate(from.getDate() - 90); break;
    case "12m": from.setMonth(from.getMonth() - 12); break;
    case "ytd": from.setMonth(0, 1); from.setHours(0, 0, 0, 0); break;
  }
  return { from, to };
}

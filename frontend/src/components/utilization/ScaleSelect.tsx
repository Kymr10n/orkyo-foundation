import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@foundation/src/components/ui/select";
import { cn } from "@foundation/src/lib/utils";

export type TimeScale = "year" | "month" | "week" | "day" | "hour";

const SCALE_LABELS: Record<TimeScale, string> = {
  year: "Year",
  month: "Month",
  week: "Week",
  day: "Day",
  hour: "Hour",
};

/** Widest to narrowest, the order the toolbar has always shown. */
export const ALL_SCALES: readonly TimeScale[] = ["year", "month", "week", "day", "hour"];

interface ScaleSelectProps {
  value: TimeScale;
  onChange: (scale: TimeScale) => void;
  /** Phone: narrower, shorter trigger to match the compact toolbar. */
  compact?: boolean;
  /**
   * The scales this surface can actually render. Offering one it cannot is worse than not
   * offering it — the calendar collapses year onto month and hour onto day, so those options
   * look like they did nothing.
   */
  scales?: readonly TimeScale[];
}

export function ScaleSelect({ value, onChange, compact = false, scales = ALL_SCALES }: ScaleSelectProps) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as TimeScale)}>
      <SelectTrigger className={cn(compact ? "h-8 w-[88px] text-xs" : "w-[120px]")}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {scales.map((scale) => (
          <SelectItem key={scale} value={scale}>
            {SCALE_LABELS[scale]}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

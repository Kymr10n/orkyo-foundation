import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@foundation/src/components/ui/select";
import { cn } from "@foundation/src/lib/utils";

export type TimeScale = "year" | "month" | "week" | "day" | "hour";

interface ScaleSelectProps {
  value: TimeScale;
  onChange: (scale: TimeScale) => void;
  /** Phone: narrower, shorter trigger to match the compact toolbar. */
  compact?: boolean;
}

export function ScaleSelect({ value, onChange, compact = false }: ScaleSelectProps) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as TimeScale)}>
      <SelectTrigger className={cn(compact ? "h-8 w-[88px] text-xs" : "w-[120px]")}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="year">Year</SelectItem>
        <SelectItem value="month">Month</SelectItem>
        <SelectItem value="week">Week</SelectItem>
        <SelectItem value="day">Day</SelectItem>
        <SelectItem value="hour">Hour</SelectItem>
      </SelectContent>
    </Select>
  );
}

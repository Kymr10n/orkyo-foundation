import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/components/ui/select";

export type TimeScale = "year" | "month" | "week" | "day" | "hour";

interface ScaleSelectProps {
  value: TimeScale;
  onChange: (scale: TimeScale) => void;
}

export function ScaleSelect({ value, onChange }: ScaleSelectProps) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as TimeScale)}>
      <SelectTrigger className="w-[120px]">
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

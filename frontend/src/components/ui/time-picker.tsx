import { useState, useCallback } from "react";
import { Clock } from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface TimePickerProps {
  /** "HH:mm" string or empty */
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
  /** Minute interval. Defaults to 5 */
  minuteStep?: number;
}

import { HOURS } from "@/lib/utils/picker-utils";

export function TimePicker({
  value,
  onChange,
  placeholder = "Pick time",
  disabled,
  id,
  minuteStep = 5,
}: TimePickerProps) {
  const [open, setOpen] = useState(false);

  const minutes = Array.from(
    { length: Math.floor(60 / minuteStep) },
    (_, i) => String(i * minuteStep).padStart(2, "0")
  );

  const [currentHour, currentMinute] = value
    ? value.split(":")
    : ["08", "00"];

  const handleHourChange = useCallback(
    (hour: string) => {
      onChange(`${hour}:${currentMinute}`);
    },
    [currentMinute, onChange]
  );

  const handleMinuteChange = useCallback(
    (minute: string) => {
      onChange(`${currentHour}:${minute}`);
    },
    [currentHour, onChange]
  );

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          variant="outline"
          disabled={disabled}
          className={cn(
            "w-full justify-start text-left font-normal h-9",
            !value && "text-muted-foreground"
          )}
        >
          <Clock className="mr-2 h-4 w-4" />
          {value || placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-3" align="start">
        <div className="flex items-center gap-2">
          <Select value={currentHour} onValueChange={handleHourChange}>
            <SelectTrigger className="w-[70px] h-8">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {HOURS.map((h) => (
                <SelectItem key={h} value={h}>
                  {h}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="text-sm font-medium">:</span>
          <Select value={currentMinute} onValueChange={handleMinuteChange}>
            <SelectTrigger className="w-[70px] h-8">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {minutes.map((m) => (
                <SelectItem key={m} value={m}>
                  {m}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </PopoverContent>
    </Popover>
  );
}

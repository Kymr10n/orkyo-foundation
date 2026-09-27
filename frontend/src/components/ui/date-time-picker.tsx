import { useState, useCallback } from "react";
import { format, parse, setHours, setMinutes, isValid } from "date-fns";
import { DATE_FORMATS, toDateTimeLocalValue } from "@foundation/src/lib/formatters";
import { CalendarIcon } from "lucide-react";

import { cn } from "@foundation/src/lib/utils";
import { Button } from "@foundation/src/components/ui/button";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import { HourMinuteSelects } from "@foundation/src/components/ui/time-picker";

interface DateTimePickerProps {
  /** ISO-like local string "YYYY-MM-DDTHH:mm" or empty */
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
}

import { MINUTES_5 } from "@foundation/src/lib/utils/picker-utils";

export function DateTimePicker({
  value,
  onChange,
  placeholder = "Pick date & time",
  disabled,
  id,
}: DateTimePickerProps) {
  const [open, setOpen] = useState(false);

  const parsed = value ? parse(value, DATE_FORMATS.DATETIME_LOCAL_INPUT, new Date()) : null;
  const date = parsed && isValid(parsed) ? parsed : undefined;

  const handleDateSelect = useCallback(
    (selected: Date | undefined) => {
      if (!selected) return;
      const hours = date ? date.getHours() : 8;
      const minutes = date ? date.getMinutes() : 0;
      const combined = setMinutes(setHours(selected, hours), minutes);
      onChange(toDateTimeLocalValue(combined));
    },
    [date, onChange]
  );

  const handleHourChange = useCallback(
    (hour: string) => {
      const base = date ?? new Date();
      const updated = setHours(base, parseInt(hour, 10));
      onChange(toDateTimeLocalValue(updated));
    },
    [date, onChange]
  );

  const handleMinuteChange = useCallback(
    (minute: string) => {
      const base = date ?? new Date();
      const updated = setMinutes(base, parseInt(minute, 10));
      onChange(toDateTimeLocalValue(updated));
    },
    [date, onChange]
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
            !date && "text-muted-foreground"
          )}
        >
          <CalendarIcon className="mr-2 h-4 w-4" />
          {date ? format(date, DATE_FORMATS.DATETIME_MEDIUM) : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          selected={date}
          defaultMonth={date}
          onSelect={handleDateSelect}
          autoFocus
        />
        <div className="border-t px-3 py-3 flex items-center gap-2">
          <span className="text-sm text-muted-foreground">Time:</span>
          <HourMinuteSelects
            hour={date ? String(date.getHours()).padStart(2, "0") : "08"}
            minute={date ? String(date.getMinutes()).padStart(2, "0") : "00"}
            minutes={MINUTES_5}
            onHourChange={handleHourChange}
            onMinuteChange={handleMinuteChange}
          />
        </div>
      </PopoverContent>
    </Popover>
  );
}

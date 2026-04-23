import { useState, useCallback } from "react";
import { format, parse, setHours, setMinutes, isValid } from "date-fns";
import { CalendarIcon } from "lucide-react";

import { cn } from "@foundation/src/lib/utils";
import { Button } from "@foundation/src/components/ui/button";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";

interface DateTimePickerProps {
  /** ISO-like local string "YYYY-MM-DDTHH:mm" or empty */
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
}

function toLocalString(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

import { HOURS, MINUTES_5 } from "@foundation/src/lib/utils/picker-utils";

export function DateTimePicker({
  value,
  onChange,
  placeholder = "Pick date & time",
  disabled,
  id,
}: DateTimePickerProps) {
  const [open, setOpen] = useState(false);

  const parsed = value ? parse(value, "yyyy-MM-dd'T'HH:mm", new Date()) : null;
  const date = parsed && isValid(parsed) ? parsed : undefined;

  const handleDateSelect = useCallback(
    (selected: Date | undefined) => {
      if (!selected) return;
      const hours = date ? date.getHours() : 8;
      const minutes = date ? date.getMinutes() : 0;
      const combined = setMinutes(setHours(selected, hours), minutes);
      onChange(toLocalString(combined));
    },
    [date, onChange]
  );

  const handleHourChange = useCallback(
    (hour: string) => {
      const base = date ?? new Date();
      const updated = setHours(base, parseInt(hour, 10));
      onChange(toLocalString(updated));
    },
    [date, onChange]
  );

  const handleMinuteChange = useCallback(
    (minute: string) => {
      const base = date ?? new Date();
      const updated = setMinutes(base, parseInt(minute, 10));
      onChange(toLocalString(updated));
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
          {date ? format(date, "dd MMM yyyy, HH:mm") : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          selected={date}
          onSelect={handleDateSelect}
          autoFocus
        />
        <div className="border-t px-3 py-3 flex items-center gap-2">
          <span className="text-sm text-muted-foreground">Time:</span>
          <Select
            value={date ? String(date.getHours()).padStart(2, "0") : "08"}
            onValueChange={handleHourChange}
          >
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
          <Select
            value={date ? String(date.getMinutes()).padStart(2, "0") : "00"}
            onValueChange={handleMinuteChange}
          >
            <SelectTrigger className="w-[70px] h-8">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MINUTES_5.map((m) => (
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

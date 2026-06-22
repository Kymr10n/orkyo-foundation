import { Button } from "@foundation/src/components/ui/button";
import {
    Popover,
    PopoverContent,
    PopoverTrigger,
} from "@foundation/src/components/ui/popover";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { cn } from "@foundation/src/lib/utils";
import { formatLocalized } from "@foundation/src/lib/formatters";
import { CalendarIcon, ChevronLeft, ChevronRight } from "lucide-react";
import type { TimeScale } from "./ScaleSelect";

interface TimeNavigatorProps {
  scale: TimeScale;
  anchorTs: Date;
  onAnchorChange: (date: Date) => void;
  onPrevious: () => void;
  onNext: () => void;
  onToday: () => void;
}

export function TimeNavigator({
  scale,
  anchorTs,
  onAnchorChange,
  onPrevious,
  onNext,
  onToday,
}: TimeNavigatorProps) {
  const formatAnchor = () => {
    // Locale-aware (date ordering + 12h/24h per the user's settings) — see formatLocalized.
    switch (scale) {
      case "year":
        return formatLocalized(anchorTs, { year: "numeric" });
      case "month":
        return formatLocalized(anchorTs, { month: "long", year: "numeric" });
      case "week":
        return formatLocalized(anchorTs, { month: "short", day: "2-digit", year: "numeric" });
      case "day":
        return formatLocalized(anchorTs, {
          weekday: "long",
          month: "short",
          day: "2-digit",
          year: "numeric",
        });
      case "hour":
        return formatLocalized(anchorTs, {
          month: "short",
          day: "2-digit",
          year: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        });
      default:
        return formatLocalized(anchorTs, { month: "short", day: "2-digit", year: "numeric" });
    }
  };

  const handleDateSelect = (selected: Date | undefined) => {
    if (selected) {
      onAnchorChange(selected);
    }
  };

  return (
    <div className="flex items-center gap-2">
      <Button
        variant="outline"
        size="icon"
        onClick={onPrevious}
        title={`Previous ${scale}`}
        aria-label={`Previous ${scale}`}
      >
        <ChevronLeft className="h-4 w-4" />
      </Button>

      <div className="flex items-center gap-1">
        <Button variant="outline" onClick={onToday}>
          {scale === "hour" ? "Now" : "Today"}
        </Button>

        <Popover>
          <PopoverTrigger asChild>
            <Button
              variant="outline"
              className={cn(
                "min-w-[200px] justify-start text-left font-normal",
              )}
            >
              <CalendarIcon className="mr-2 h-4 w-4" />
              {formatAnchor()}
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-auto p-0">
            <Calendar
              mode="single"
              selected={anchorTs}
              onSelect={handleDateSelect}
              autoFocus
            />
          </PopoverContent>
        </Popover>
      </div>

      <Button
        variant="outline"
        size="icon"
        onClick={onNext}
        title={`Next ${scale}`}
        aria-label={`Next ${scale}`}
      >
        <ChevronRight className="h-4 w-4" />
      </Button>
    </div>
  );
}

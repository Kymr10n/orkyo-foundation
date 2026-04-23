import { Button } from "@foundation/src/components/ui/button";
import {
    Popover,
    PopoverContent,
    PopoverTrigger,
} from "@foundation/src/components/ui/popover";
import { Calendar } from "@foundation/src/components/ui/calendar";
import { cn } from "@foundation/src/lib/utils";
import { format } from "date-fns";
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
    switch (scale) {
      case "year":
        return format(anchorTs, "yyyy");
      case "month":
        return format(anchorTs, "MMMM yyyy");
      case "week":
        return format(anchorTs, "MMM dd, yyyy");
      case "day":
        return format(anchorTs, "EEEE, MMM dd, yyyy");
      case "hour":
        return format(anchorTs, "MMM dd, yyyy HH:00");
      default:
        return format(anchorTs, "MMM dd, yyyy");
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
      >
        <ChevronRight className="h-4 w-4" />
      </Button>
    </div>
  );
}

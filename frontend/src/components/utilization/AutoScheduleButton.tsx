import { WandSparkles } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";

interface Props {
  disabled?: boolean;
  loading?: boolean;
  onClick: () => void;
}

export function AutoScheduleButton({ disabled, loading, onClick }: Props) {
  return (
    <TooltipProvider delayDuration={300}>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={onClick}
            disabled={disabled || loading}
            aria-label="Auto-schedule unscheduled requests"
          >
            <WandSparkles
              className={`h-4 w-4 ${loading ? "animate-pulse" : ""}`}
            />
          </Button>
        </TooltipTrigger>
        <TooltipContent>Auto-schedule unscheduled requests</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}

import { AlertCircle, AlertTriangle } from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import { Alert, AlertDescription, AlertTitle } from "@foundation/src/components/ui/alert";
import { cn } from "@foundation/src/lib/utils/utils";
import type { Conflict } from "@foundation/src/types/requests";

const hasError = (conflicts: Conflict[]) => conflicts.some((c) => c.severity === "error");

/**
 * Tailwind background class for a tab/badge dot, coloured by the worst severity among
 * `conflicts` (error → destructive, warning → amber). Returns null when there are none.
 */
export function conflictDotClass(conflicts: Conflict[]): string | null {
  if (conflicts.length === 0) return null;
  return hasError(conflicts) ? "bg-destructive" : "bg-amber-500";
}

/**
 * Inline icon + tooltip flagging the conflict(s) on a single row (a person, the space, or a
 * requirement) in the Edit Request form. Renders nothing when there are no conflicts. The worst
 * severity drives the icon/colour; the tooltip lists every conflict message.
 */
export function ConflictIndicator({
  conflicts,
  className,
}: {
  conflicts: Conflict[];
  className?: string;
}) {
  if (conflicts.length === 0) return null;
  const error = hasError(conflicts);
  const Icon = error ? AlertCircle : AlertTriangle;
  return (
    <TooltipProvider delayDuration={200}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            data-testid="conflict-indicator"
            aria-label={error ? "Has a conflict" : "Has a warning"}
            className={cn("inline-flex shrink-0", error ? "text-destructive" : "text-amber-500", className)}
          >
            <Icon className="h-4 w-4" />
          </span>
        </TooltipTrigger>
        <TooltipContent>
          <ul className="space-y-1 text-xs">
            {conflicts.map((c) => (
              <li key={c.id}>{c.message}</li>
            ))}
          </ul>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}

/**
 * Summary banner listing every conflict on the request being edited — including request-level
 * ones (e.g. timing) that don't map to a single row. Renders nothing when there are no conflicts.
 */
export function ConflictBanner({ conflicts }: { conflicts: Conflict[] }) {
  if (conflicts.length === 0) return null;
  const error = hasError(conflicts);
  const Icon = error ? AlertCircle : AlertTriangle;
  return (
    <Alert
      variant={error ? "destructive" : "warning"}
      data-testid="conflict-banner"
      className="mx-6 mb-2"
    >
      <Icon className="h-4 w-4" />
      <AlertTitle>
        {conflicts.length} {conflicts.length === 1 ? "conflict" : "conflicts"} on this request
      </AlertTitle>
      <AlertDescription>
        <ul className="list-disc space-y-0.5 pl-4">
          {conflicts.map((c) => (
            <li key={c.id}>{c.message}</li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}

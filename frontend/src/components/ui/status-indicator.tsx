import { AlertCircle, AlertTriangle } from "lucide-react";
import type { ComponentType, ReactNode } from "react";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import { Alert, AlertDescription, AlertTitle } from "@foundation/src/components/ui/alert";
import { cn } from "@foundation/src/lib/utils/utils";

/**
 * Domain-agnostic severity indicators shared across dialogs. The request editor's
 * conflict badges, the assignment-validation lists, and any future per-row/per-tab
 * status flagging all render through these primitives so the icon/colour mapping
 * stays consistent. Domain code maps its own model onto {@link StatusItem}.
 */
export type StatusSeverity = "error" | "warning";

export interface StatusItem {
  id: string;
  message: string;
  severity: StatusSeverity;
}

/** Worst severity among `items` (error outranks warning), or null when empty. */
export function worstSeverity(items: StatusItem[]): StatusSeverity | null {
  if (items.length === 0) return null;
  return items.some((i) => i.severity === "error") ? "error" : "warning";
}

/**
 * Tailwind background class for a tab/badge dot, coloured by the worst severity among
 * `items` (error → destructive, warning → amber). Returns null when there are none.
 */
export function severityDotClass(items: StatusItem[]): string | null {
  switch (worstSeverity(items)) {
    case "error":
      return "bg-destructive";
    case "warning":
      return "bg-amber-500";
    default:
      return null;
  }
}

/** Lucide icon + foreground text colour for a severity. */
function severityVisuals(severity: StatusSeverity): {
  Icon: ComponentType<{ className?: string }>;
  textClass: string;
} {
  return severity === "error"
    ? { Icon: AlertCircle, textClass: "text-destructive" }
    : { Icon: AlertTriangle, textClass: "text-amber-500" };
}

/** Badge tint (bg + text) per severity — same red/amber hues as the request-calendar severity source. */
const SEVERITY_BADGE_CLASS: Record<StatusSeverity, string> = {
  error: "bg-red-500/10 text-red-700 dark:text-red-400",
  warning: "bg-amber-500/10 text-amber-700 dark:text-amber-400",
};

/** Neutral, presentation-level label per severity. */
const SEVERITY_LABEL: Record<StatusSeverity, string> = {
  error: "Error",
  warning: "Warning",
};

export interface SeverityPresentation {
  /** Lucide icon component for the severity — caller sizes/positions it. */
  icon: ComponentType<{ className?: string }>;
  /** Foreground colour class for the standalone icon. */
  iconClass: string;
  /** Badge tint class (bg + text). */
  badgeClass: string;
  /** Presentation label ("Error" / "Warning"). */
  label: string;
}

/**
 * Single source for severity icon/colour/label presentation, shared by the
 * conflicts list and the calendar event badges. Consolidates the hand-rolled
 * `getSeverity*` switches so the red/amber mapping stays consistent with the
 * status indicators above and the request-calendar severity colours.
 */
export function severityPresentation(severity: StatusSeverity): SeverityPresentation {
  const { Icon, textClass } = severityVisuals(severity);
  return {
    icon: Icon,
    iconClass: textClass,
    badgeClass: SEVERITY_BADGE_CLASS[severity],
    label: SEVERITY_LABEL[severity],
  };
}

/**
 * Inline icon + tooltip flagging the status item(s) on a single row. Renders nothing
 * when there are none. The worst severity drives the icon/colour; the tooltip lists
 * every message.
 */
export function StatusIndicator({
  items,
  className,
  testId = "status-indicator",
}: {
  items: StatusItem[];
  className?: string;
  /** Overridable for callers that assert on a domain-specific id. */
  testId?: string;
}) {
  const severity = worstSeverity(items);
  if (!severity) return null;
  const { Icon, textClass } = severityVisuals(severity);
  return (
    <TooltipProvider delayDuration={200}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            data-testid={testId}
            aria-label={severity === "error" ? "Has a conflict" : "Has a warning"}
            className={cn("inline-flex shrink-0", textClass, className)}
          >
            <Icon className="h-4 w-4" />
          </span>
        </TooltipTrigger>
        <TooltipContent>
          <ul className="space-y-1 text-xs">
            {items.map((i) => (
              <li key={i.id}>{i.message}</li>
            ))}
          </ul>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}

/**
 * Summary banner (an `Alert`) listing every status item. Renders nothing when there
 * are none. The worst severity drives the variant/icon. `title` defaults to a generic
 * "N issue(s)" count; callers pass their own copy.
 */
export function StatusBanner({
  items,
  title,
  className,
  testId = "status-banner",
}: {
  items: StatusItem[];
  title?: ReactNode;
  className?: string;
  testId?: string;
}) {
  const severity = worstSeverity(items);
  if (!severity) return null;
  const { Icon } = severityVisuals(severity);
  return (
    <Alert
      variant={severity === "error" ? "destructive" : "warning"}
      data-testid={testId}
      className={className}
    >
      <Icon className="h-4 w-4" />
      <AlertTitle>
        {title ?? `${items.length} ${items.length === 1 ? "issue" : "issues"}`}
      </AlertTitle>
      <AlertDescription>
        <ul className="list-disc space-y-0.5 pl-4">
          {items.map((i) => (
            <li key={i.id}>{i.message}</li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}

/**
 * Bare list of severity-coloured message rows (icon + text), no `Alert` chrome.
 * Renders nothing when there are none. Used inline beneath a field/row.
 */
export function StatusMessageList({
  items,
  className,
}: {
  items: StatusItem[];
  className?: string;
}) {
  if (items.length === 0) return null;
  return (
    <ul className={cn("space-y-0.5 mt-1", className)}>
      {items.map((i) => {
        const { Icon, textClass } = severityVisuals(i.severity);
        return (
          <li key={i.id} className={cn("flex items-start gap-1 text-xs", textClass)}>
            <Icon className="h-3.5 w-3.5 mt-0.5 shrink-0" />
            <span>{i.message}</span>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * Small absolute-positioned dot for flagging a tab (or any `relative` container) that
 * owns status items. Renders nothing when `dotClass` is null. Pair with `severityDotClass`
 * and a `relative` `TabsTrigger`.
 */
export function TabIndicatorDot({
  dotClass,
  label,
}: {
  dotClass: string | null;
  label: string;
}) {
  if (!dotClass) return null;
  return (
    <span
      className={cn("absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full", dotClass)}
      aria-label={label}
    />
  );
}

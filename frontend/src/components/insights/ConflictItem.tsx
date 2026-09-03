import { Bot } from "lucide-react";
import { format } from "date-fns";
import React from "react";
import { DATE_FORMATS } from "@foundation/src/lib/formatters";
import { cn } from "@foundation/src/lib/utils";
import { severityPresentation } from "@foundation/src/components/ui/status-indicator";
import type { Conflict, Request } from "@foundation/src/types/requests";

export type ConflictWithRequest = Conflict & { request: Request };

/**
 * Human label for a conflict kind. Lives beside the card that renders it so the
 * Conflicts tab and the schedule grid's popover cannot drift apart on wording.
 */
export function getConflictKindLabel(kind: string): string {
  switch (kind) {
    case "overlap":
      return "Scheduling Overlap";
    case "below_min_duration":
      return "Below Minimum Duration";
    case "before_earliest_start":
      return "Before Earliest Start";
    case "after_latest_end":
      return "After Latest End";
    case "connector_mismatch":
      return "Capability Mismatch";
    case "load_exceeded":
      return "Load Exceeded";
    case "size_mismatch":
      return "Size Mismatch";
    case "capacity_exceeded":
      return "Capacity Exceeded";
    case "dependency_violation":
      return "Dependency Violation";
    default:
      return kind;
  }
}

/**
 * One conflict, as a card: severity, kind, message, the scheduled window, and a link
 * to the request on the other side of it.
 *
 * Shared by the Conflicts tab (a virtualized list of these) and the schedule grid's
 * conflict popover, so a conflict reads the same wherever the user meets it. The
 * assistant link is optional because the popover has no assistant context.
 */
export const ConflictItem = React.memo(function ConflictItem({
  item,
  isHighlighted = false,
  onOpen,
  onAskAssistant,
  assistantAvailable = false,
  peerRequest,
}: {
  item: ConflictWithRequest;
  isHighlighted?: boolean;
  onOpen: (request: Request) => void;
  onAskAssistant?: (item: ConflictWithRequest) => void;
  assistantAvailable?: boolean;
  peerRequest?: Request;
}) {
  const { icon: SeverityIcon, iconClass, badgeClass, label } = severityPresentation(item.severity);
  return (
    <div
      role="button"
      tabIndex={0}
      className={`border rounded-lg p-4 bg-card text-card-foreground shadow-xs hover:bg-accent/50 transition-colors cursor-pointer ${
        isHighlighted ? "ring-2 ring-destructive/60 bg-destructive/5" : ""
      }`}
      onClick={() => onOpen(item.request)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onOpen(item.request);
        }
      }}
    >
      <div className="flex items-start gap-3">
        <div className="mt-0.5">
          <SeverityIcon className={cn("w-5 h-5", iconClass)} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-2">
            <h3 className="font-semibold truncate">{item.request.name}</h3>
            <span className={cn("px-2 py-0.5 rounded text-xs font-medium", badgeClass)}>
              {label}
            </span>
            <span className="px-2 py-0.5 rounded text-xs font-medium bg-muted">
              {getConflictKindLabel(item.kind)}
            </span>
          </div>
          <p className="text-sm text-muted-foreground mb-3">{item.message}</p>
          {item.request.startTs && item.request.endTs && (
            <div className="text-xs text-muted-foreground">
              <span className="inline-flex items-center gap-1">
                <span>Scheduled:</span>
                <span className="font-medium">
                  {format(new Date(item.request.startTs), DATE_FORMATS.DATETIME_HEADER)} –{" "}
                  {format(new Date(item.request.endTs), DATE_FORMATS.DATETIME_HEADER)}
                </span>
              </span>
            </div>
          )}
          <div className="mt-3 flex flex-wrap items-center gap-4">
            {peerRequest && (
              <button
                className="text-xs text-primary underline-offset-2 hover:underline"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpen(peerRequest);
                }}
              >
                View other request: {peerRequest.name}
              </button>
            )}
            {assistantAvailable && (
              // The card itself opens the request editor, so this has to stop the click
              // from bubbling — same as the peer link above.
              <button
                className="text-xs text-primary underline-offset-2 hover:underline inline-flex items-center gap-1"
                onClick={(e) => {
                  e.stopPropagation();
                  onAskAssistant?.(item);
                }}
                aria-label={`Ask the assistant about the conflict on ${item.request.name}`}
              >
                <Bot className="h-3.5 w-3.5" />
                Ask the assistant
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
});

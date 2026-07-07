import type { Request } from "@foundation/src/types/requests";
import { getRequestIcon, getPlanningModeIcon } from "@foundation/src/constants";
import { formatDateDisplay } from "@foundation/src/lib/utils/utils";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";

export interface UtilizationAgendaProps {
  /** Scheduled requests for the active window. */
  requests: Request[];
  /** Open a request (tap-to-act on touch). */
  onOpen: (request: Request) => void;
  emptyMessage?: string;
}

/**
 * Phone replacement for the utilization timeline grid: a chronological,
 * drag-free list of the scheduled work in the active window. Reuses the same
 * request data the grid consumes — no grid internals are touched. Tapping a
 * card opens the request editor, the touch-equivalent of clicking a scheduled
 * block on desktop.
 */
export function UtilizationAgenda({
  requests,
  onOpen,
  emptyMessage = "No scheduled work in this window.",
}: UtilizationAgendaProps) {
  const sorted = requests
    .filter((r) => r.startTs)
    .sort((a, b) => (a.startTs! < b.startTs! ? -1 : a.startTs! > b.startTs! ? 1 : 0));

  if (sorted.length === 0) {
    return <EmptyState message={emptyMessage} className="text-sm" />;
  }

  return (
    <div className="h-full space-y-2 overflow-auto p-1" role="list">
      {sorted.map((request) => {
        const Icon = getRequestIcon(request.icon) ?? getPlanningModeIcon(request.planningMode);
        return (
          <div
            key={request.id}
            role="listitem"
            onClick={() => onOpen(request)}
            className="cursor-pointer rounded-lg border bg-card p-3 shadow-xs hover:bg-accent/40"
          >
            <div className="flex items-center justify-between gap-2">
              <div className="flex min-w-0 items-center gap-2">
                <Icon className="h-4 w-4 flex-shrink-0 text-muted-foreground" />
                <span className="truncate font-medium">{request.name}</span>
              </div>
              <RequestStatusBadge status={request.status} />
            </div>
            <div className="mt-1 text-xs text-muted-foreground">
              {request.startTs && request.endTs
                ? `${formatDateDisplay(request.startTs)} — ${formatDateDisplay(request.endTs)}`
                : "Unscheduled"}
            </div>
          </div>
        );
      })}
    </div>
  );
}

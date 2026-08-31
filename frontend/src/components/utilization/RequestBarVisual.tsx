import { AlertCircle, Layers } from "lucide-react";
import type { Request } from "@foundation/src/types/requests";
import { STATUS_CELL_CLASS, STATUS_BORDER_CLASS, STATUS_FILL_CLASS, STATUS_PATTERN_CLASS } from "./schedule-colors";

/**
 * The look of a scheduled request bar, in one place.
 *
 * Two things render a bar: the real one in the grid row, and the lightweight clone the
 * DragOverlay carries under the pointer while it is dragged. They must be visually
 * identical — a clone that drifts from the bar makes the drag look like it picked up
 * something else — so both build from the pieces here rather than repeating the markup.
 */
export type RequestBarStatus = 'assigned' | 'overbooked';

/** Shape, type scale and clipping. Position and size are the caller's business. */
export const REQUEST_BAR_BASE_CLASS =
  "rounded border text-xs text-foreground p-1 overflow-hidden";

/** Status-tinted track plus coloured border, from the shared schedule-colors tokens. */
export function requestBarToneClass(status: RequestBarStatus): string {
  return `${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]}`;
}

/**
 * The translucent fill and the diagonal hatch that carries the overbooked state without
 * relying on colour alone (WCAG 1.4.1). Absolutely positioned, so the caller is relative.
 */
export function RequestBarLayers({ status }: { status: RequestBarStatus }) {
  return (
    <>
      {STATUS_FILL_CLASS[status] && (
        <div className={`absolute inset-0 ${STATUS_FILL_CLASS[status]}`} aria-hidden="true" />
      )}
      {STATUS_PATTERN_CLASS[status] && (
        <div className={`absolute inset-0 ${STATUS_PATTERN_CLASS[status]}`} aria-hidden="true" />
      )}
    </>
  );
}

/** Conflict marker, planning-mode and child-request badges, then the request's name. */
export function RequestBarLabel({ request, hasConflict }: { request: Request; hasConflict: boolean }) {
  return (
    <div className="relative z-10 flex items-center gap-1">
      {hasConflict && <AlertCircle className="w-3 h-3 flex-shrink-0" />}
      {request.planningMode === 'summary' && (
        <Layers className="w-3 h-3 flex-shrink-0 opacity-70" />
      )}
      {request.parentRequestId && (
        <span className="w-1.5 h-1.5 rounded-full bg-current opacity-50 flex-shrink-0" />
      )}
      <div className="truncate font-medium">{request.name}</div>
    </div>
  );
}

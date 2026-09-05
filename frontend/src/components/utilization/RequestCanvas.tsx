import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Maximize, ZoomIn, ZoomOut } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { getRequests } from "@foundation/src/lib/api/request-api";
import type { Conflict, Request } from "@foundation/src/types/requests";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeScale } from "./ScaleSelect";
import { TimelineGridShell } from "./TimelineGridShell";
import { RequestTimelineRow } from "./RequestTimelineRow";
import { NowLine } from "./NowLine";
import { DEFAULT_COLUMN_MIN_WIDTH_PX } from "./TimelineRow";
import { generateTimeColumns, parseTimeToHour, type WorkingHoursConfig } from "./time-grid-utils";
import { enrichColumnsWithOffTime } from "./time-grid-offtime";
import { groupRequestsByParent, selectCanvasRequests } from "./request-canvas-groups";

/**
 * Zoom is a multiplier on the column width every grid always had. 1× is fit-to-width — the
 * columns stretch to fill the viewport exactly as on the other tabs — and each step widens their
 * minimum so the canvas grows past the viewport and scrolls; the shell keeps header and body in
 * step. Capped at 4×: beyond that a week's day column is wider than most windows.
 */
export const ZOOM_MIN = 1;
export const ZOOM_MAX = 4;
export const ZOOM_STEP = 0.5;
export const BASE_COL_PX = DEFAULT_COLUMN_MIN_WIDTH_PX;

const EMPTY_CONFLICTS: readonly Conflict[] = [];
const EMPTY_REQUESTS: readonly Request[] = [];

function clampZoom(zoom: number): number {
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, zoom));
}

interface RequestCanvasProps {
  /** The scheduled requests that survive the tab's search and filters. */
  requests: readonly Request[];
  /**
   * Every request the page already holds (scheduled window ∪ backlog), used to name a row's
   * parent before falling back to the request list.
   */
  lookup: readonly Request[];
  /** The page's conflict registry: request id → conflicts. Colours the bars. */
  conflicts: Map<string, Conflict[]>;
  scale: TimeScale;
  anchorTs: Date;
  /** Live wall-clock "now" (epoch ms) shared with the page — drives the Now line. */
  nowMs: number;
  siteId: string | null;
  isLoading?: boolean;
  offTimeRanges?: readonly OffTimeRange[];
  weekendsEnabled?: boolean;
  workingHoursEnabled?: boolean;
  workingDayStart?: string;
  workingDayEnd?: string;
  onRequestClick?: (requestId: string, position?: { x: number; y: number }) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  /** The key for the bars, left of the toolbar. */
  legend?: ReactNode;
  /** Search and filters, right of the toolbar next to the zoom buttons. */
  filterBar?: ReactNode;
}

/**
 * The Requests canvas: the scheduled requests of the visible window as rows, one bar each,
 * grouped under their parent request, on the same time columns as the other utilization grids.
 *
 * Where the Stations and Assets grids answer "what is this resource doing", this one answers
 * "when does this task run" — the same requests turned ninety degrees. It is read-only: a bar
 * opens the request, and scheduling stays on the surfaces whose rows are resources.
 *
 * Built on `TimelineGridShell` like the other two grids; the only thing added to the shell is a
 * per-column minimum width, which is all zoom needs.
 */
export function RequestCanvas({
  requests,
  lookup,
  conflicts,
  scale,
  anchorTs,
  nowMs,
  siteId,
  isLoading = false,
  offTimeRanges = [],
  weekendsEnabled = false,
  workingHoursEnabled = false,
  workingDayStart = "08:00",
  workingDayEnd = "17:00",
  onRequestClick,
  onRequestDoubleClick,
  legend,
  filterBar,
}: RequestCanvasProps) {
  const workingHours: WorkingHoursConfig | null = workingHoursEnabled
    ? { enabled: true, start: parseTimeToHour(workingDayStart), end: parseTimeToHour(workingDayEnd) }
    : null;
  const columns = useMemo(
    () =>
      enrichColumnsWithOffTime(
        generateTimeColumns(scale, anchorTs, weekendsEnabled, workingHours),
        offTimeRanges,
      ),
    // workingHours is derived from the two string props + the flag.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [scale, anchorTs, weekendsEnabled, workingHoursEnabled, workingDayStart, workingDayEnd, offTimeRanges],
  );
  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();

  // Names only. A parent can be missing from the page's feeds — its own dates outside the window,
  // or a dateless container — and a group headed by an id tells nobody what the tasks belong to.
  // The same query the Requests page runs, so the two share one cache entry. The tab unmounts
  // its content when another is shown, so the other three tabs never run it.
  const { data: requestList = EMPTY_REQUESTS } = useQuery({
    queryKey: qk.requests.list(siteId),
    queryFn: () => getRequests(true, siteId ?? undefined),
    staleTime: STALE.STANDARD,
  });

  const resolveParent = useMemo(() => {
    const fromPage = new Map(lookup.map((r) => [r.id, r]));
    const fromList = new Map(requestList.map((r) => [r.id, r]));
    return (id: string) => fromPage.get(id) ?? fromList.get(id);
  }, [lookup, requestList]);

  const groups = useMemo(
    () => groupRequestsByParent(selectCanvasRequests(requests, viewStartMs, viewEndMs), resolveParent),
    [requests, viewStartMs, viewEndMs, resolveParent],
  );

  // ── Zoom ──────────────────────────────────────────────────────────────────
  const [zoom, setZoom] = useState(ZOOM_MIN);
  const columnMinWidthPx = Math.round(BASE_COL_PX * zoom);
  const zoomBy = useCallback((delta: number) => setZoom((z) => clampZoom(z + delta)), []);

  // Ctrl/⌘ + wheel zooms, the way maps and editors do. A plain wheel keeps scrolling the grid.
  // Registered by hand because React's onWheel is passive and cannot preventDefault, and without
  // preventDefault the browser zooms the whole page on top of the canvas.
  const wrapperRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = wrapperRef.current;
    if (!el) return;
    const onWheel = (e: WheelEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      e.preventDefault();
      zoomBy(e.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP);
    };
    el.addEventListener("wheel", onWheel, { passive: false });
    return () => el.removeEventListener("wheel", onWheel);
  }, [zoomBy]);

  const toolbar = (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b px-3 py-2 text-xs text-muted-foreground shrink-0">
      {legend ?? <span />}
      <div className="flex flex-wrap items-center gap-2">
        {filterBar}
        <div className="flex items-center gap-1" role="group" aria-label="Zoom">
          <Button
            variant="outline" size="icon" aria-label="Zoom out"
            onClick={() => zoomBy(-ZOOM_STEP)}
            disabled={zoom <= ZOOM_MIN}
          >
            <ZoomOut className="h-4 w-4" />
          </Button>
          <Button
            variant="outline" size="icon" aria-label="Reset zoom"
            onClick={() => setZoom(ZOOM_MIN)}
            disabled={zoom === ZOOM_MIN}
          >
            <Maximize className="h-4 w-4" />
          </Button>
          <Button
            variant="outline" size="icon" aria-label="Zoom in"
            onClick={() => zoomBy(ZOOM_STEP)}
            disabled={zoom >= ZOOM_MAX}
          >
            <ZoomIn className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  );

  return (
    <div ref={wrapperRef} className="flex flex-1 flex-col overflow-hidden" data-testid="request-canvas">
      <TimelineGridShell<Request>
        labelHeader="Task"
        columns={columns}
        scale={scale}
        groups={groups}
        collapseIdPrefix="requests"
        getRowId={(r) => r.id}
        emptyMessage="No scheduled tasks in this period."
        isLoading={isLoading}
        columnMinWidthPx={columnMinWidthPx}
        toolbar={toolbar}
        bodyOverlay={<NowLine nowMs={nowMs} viewStartMs={viewStartMs} viewEndMs={viewEndMs} />}
        renderRow={(request) => (
          <RequestTimelineRow
            request={request}
            columns={columns}
            columnMinWidthPx={columnMinWidthPx}
            conflicts={conflicts.get(request.id) ?? EMPTY_CONFLICTS}
            onRequestClick={onRequestClick}
            onRequestDoubleClick={onRequestDoubleClick}
          />
        )}
      />
    </div>
  );
}

import { useCallback, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight, Network, ZoomIn, ZoomOut, Maximize } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { getSitePlan, type RequestPlanChild } from "@foundation/src/lib/api/request-plan-api";
import type { RequestDependency } from "@foundation/src/lib/api/request-dependency-api";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { computePlanLayout, PLAN_NODE_HEIGHT, PLAN_NODE_WIDTH } from "@foundation/src/domain/plan-layout";
import { clampToViewPercent } from "@foundation/src/domain/scheduling/schedule-selectors";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeScale } from "@foundation/src/components/utilization/ScaleSelect";
import type { TimeColumn } from "@foundation/src/components/utilization/scheduler-types";
import { useTimeColumns } from "@foundation/src/components/utilization/useTimeColumns";
import { viewPositionPercent } from "@foundation/src/components/utilization/time-grid-utils";
import {
  columnHeaderTintClass,
  columnHeaderTitle,
  columnTintClass,
} from "@foundation/src/components/utilization/TimelineRow";
import { OFFTIME_TINT_CLASS, PROBLEM_HATCH_CLASS } from "@foundation/src/components/utilization/schedule-colors";
import { RequestBarLabel } from "@foundation/src/components/utilization/RequestBarVisual";
import {
  getEventConflictSeverity,
  REQUEST_LEGEND,
  SEVERITY_SWATCH,
  STATUS_SWATCH,
  type ConflictSeverity,
} from "@foundation/src/components/utilization/request-calendar-events";
import { PlanEdgeLayer, type PlanRect } from "./PlanEdgeLayer";
import { PlanNodeCard } from "./PlanNodeCard";
import { collectViolatingEdgeIds } from "./plan-conflicts";

const ZOOM_MIN = 0.5;
const ZOOM_MAX = 2;
const ZOOM_STEP = 0.25;
const CANVAS_PADDING = 32;

/** Band chrome: the header strip and the breathing room around an expanded band's graph. */
const BAND_HEADER_PX = 44;
const BAND_PADDING_PX = 16;
/** Bands are contiguous like the grids' group rows; the header's border-b is the separator. */
const BAND_GAP_PX = 0;

/** Timeline mode: columns stretch to fill the viewport, never narrower than this. */
const TIMELINE_MIN_COL_PX = 90;
const TIMELINE_HEADER_PX = 28;
const BAR_HEIGHT_PX = 28;
const ROW_PX = 36;
/** A one-hour task at month scale is sub-pixel; a bar this thin is still clickable. */
const MIN_BAR_PX = 3;

/** Trailing band for tasks with no parent, mirroring the tree view's convention. */
const UNGROUPED_ID = "ungrouped";
const UNGROUPED_NAME = "Ungrouped";

export type SitePlanView = "structure" | "timeline";

interface Band {
  id: string;
  name: string;
  children: RequestPlanChild[];
  /** Edges with both ends inside this band — what the structure layout is computed over. */
  edges: RequestDependency[];
}

interface PlacedBand {
  band: Band;
  top: number;
  bodyHeight: number;
  expanded: boolean;
  hasCycle: boolean;
  /** Timeline mode: work the band holds but the canvas cannot draw. */
  unscheduled: number;
  outsideWindow: number;
}

/**
 * The site-wide dependency canvas: every group as a band, every task as a node, and the edges
 * among them — including edges that cross groups, which the per-group planner can only count.
 *
 * Two views over one band structure. **Structure** lays each band out by dependency depth
 * (`computePlanLayout`, fixed-size cards) — the per-group planner's drawing, site-wide.
 * **Timeline** puts the bands on a date grid that follows the page's scale and anchor: each
 * dated task is a bar spanning its actual start→end, stretched or compressed by the scale,
 * with the same dependency arrows between the bars. A task without dates has no place on a
 * time axis, so timeline mode counts it on the band header instead of drawing it.
 *
 * Read-only either way: a tenant has thousands of tasks, so bands start collapsed and editing
 * stays in the per-group planner each band header links to.
 *
 * Expansion is deliberately local state, not the app store's `collapsedGroupIds`: that store
 * records exceptions from an expanded-by-default world, and this canvas is the opposite.
 */
export function SitePlanCanvas({
  siteId,
  view,
  onOpenRequest,
  onOpenGroupPlanner,
  scale,
  anchorTs,
  nowMs,
  offTimeRanges = [],
  weekendsEnabled = false,
  workingHoursEnabled = false,
  workingDayStart,
  workingDayEnd,
}: {
  siteId: string | null;
  view: SitePlanView;
  /** Double-click on a task. The page routes it to the request editor deep link. */
  onOpenRequest: (requestId: string) => void;
  /** The band header's "Sequence" action — the existing per-group planner, for editing. */
  onOpenGroupPlanner: (groupId: string) => void;
  /** Timeline inputs, shared with the page's other tabs. Unused in structure view. */
  scale: TimeScale;
  anchorTs: Date;
  nowMs: number;
  offTimeRanges?: readonly OffTimeRange[];
  weekendsEnabled?: boolean;
  workingHoursEnabled?: boolean;
  workingDayStart?: string;
  workingDayEnd?: string;
}) {
  const { isPhone } = useBreakpoint();
  const [zoom, setZoom] = useState(1);
  const [expandedIds, setExpandedIds] = useState<ReadonlySet<string>>(() => new Set());
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  // The scroller's inner width, so the timeline can stretch to fill it (fit-to-width, with the
  // per-column minimum as the floor). A callback ref rather than an effect: the scroller only
  // exists once data has loaded, after the early returns below.
  const [containerWidth, setContainerWidth] = useState(0);
  const observerRef = useRef<ResizeObserver | null>(null);
  const attachScroller = useCallback((el: HTMLDivElement | null) => {
    scrollRef.current = el;
    observerRef.current?.disconnect();
    observerRef.current = null;
    if (!el) return;
    setContainerWidth(el.clientWidth);
    if (typeof ResizeObserver !== "undefined") {
      observerRef.current = new ResizeObserver(() => setContainerWidth(el.clientWidth));
      observerRef.current.observe(el);
    }
  }, []);

  const { data, isLoading, error } = useQuery({
    queryKey: qk.requests.sitePlan(siteId),
    queryFn: () => getSitePlan(siteId),
    staleTime: STALE.OPERATIONAL,
  });

  const { conflictsByRequest } = useConflictRegistry();
  const violatingEdgeIds = useMemo(
    () => collectViolatingEdgeIds(data?.edges ?? [], conflictsByRequest),
    [data?.edges, conflictsByRequest],
  );

  // The date grid, following the page's selector — the same columns the utilization grids use.
  const columns = useTimeColumns({
    scale, anchorTs, weekendsEnabled, workingHoursEnabled, workingDayStart, workingDayEnd, offTimeRanges,
  });
  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();

  // ── Bands ─────────────────────────────────────────────────────────────────
  const bands = useMemo<Band[]>(() => {
    if (!data) return [];
    const byParent = new Map<string, RequestPlanChild[]>();
    for (const child of data.children) {
      const key = child.parentRequestId ?? UNGROUPED_ID;
      const list = byParent.get(key);
      if (list) list.push(child);
      else byParent.set(key, [child]);
    }
    const bandEdges = (bandId: string) => {
      const members = new Set((byParent.get(bandId) ?? []).map((c) => c.id));
      return (data.edges ?? []).filter(
        (e) => members.has(e.predecessorRequestId) && members.has(e.successorRequestId),
      );
    };

    const ordered: Band[] = data.groups
      .filter((g) => byParent.has(g.id))
      .map((g) => ({ id: g.id, name: g.name, children: byParent.get(g.id)!, edges: bandEdges(g.id) }));
    if (byParent.has(UNGROUPED_ID)) {
      ordered.push({
        id: UNGROUPED_ID,
        name: UNGROUPED_NAME,
        children: byParent.get(UNGROUPED_ID)!,
        edges: bandEdges(UNGROUPED_ID),
      });
    }
    return ordered;
  }, [data]);

  // ── Geometry: per-band layouts stacked with a running offset ──────────────
  // One placedBands/rects shape whichever view produced it, so the edge layer and the band
  // headers do not care. Structure places fixed-size cards by dependency depth; timeline
  // places duration-wide bars by date, one per row.
  const geometry = useMemo(() => {
    const isTimeline = view === "timeline";
    // Timeline runs flush inside the card like the other grids, so the whole scroller
    // width is drawable.
    const usable = Math.max(0, containerWidth);
    const timelineWidth = Math.max(columns.length * TIMELINE_MIN_COL_PX, usable);
    // Content coordinates only: the date header is chrome the render places above this,
    // so bands start at zero and the edge layer's rects need no header offset.
    let y = 0;
    let maxCardWidth = 0;
    const rectsById = new Map<string, PlanRect>();
    const placedBands: PlacedBand[] = [];

    for (const band of bands) {
      const expanded = expandedIds.has(band.id);
      const top = y;
      let bodyHeight = 0;
      let hasCycle = false;
      let unscheduled = 0;
      let outsideWindow = 0;

      if (isTimeline) {
        const dated = band.children
          .filter((c) => c.startTs && c.endTs)
          .sort((a, b) => new Date(a.startTs!).getTime() - new Date(b.startTs!).getTime());
        unscheduled = band.children.length - dated.length;
        const visible = dated.filter((c) => {
          const s = new Date(c.startTs!).getTime();
          const e = new Date(c.endTs!).getTime();
          return e > viewStartMs && s < viewEndMs;
        });
        outsideWindow = dated.length - visible.length;

        if (expanded) {
          bodyHeight = visible.length * ROW_PX + 2 * BAND_PADDING_PX;
          visible.forEach((c, row) => {
            const { leftPercent, widthPercent } = clampToViewPercent(
              new Date(c.startTs!).getTime(), new Date(c.endTs!).getTime(), viewStartMs, viewEndMs);
            rectsById.set(c.id, {
              x: (leftPercent / 100) * timelineWidth,
              y: top + BAND_HEADER_PX + BAND_PADDING_PX + row * ROW_PX,
              width: Math.max((widthPercent / 100) * timelineWidth, MIN_BAR_PX),
              height: BAR_HEIGHT_PX,
            });
          });
        }
      } else if (expanded) {
        const layout = computePlanLayout(band.children, band.edges);
        hasCycle = layout.hasCycle;
        bodyHeight = layout.height + 2 * BAND_PADDING_PX;
        for (const node of layout.nodes) {
          rectsById.set(node.id, {
            x: node.x + BAND_PADDING_PX,
            y: node.y + top + BAND_HEADER_PX + BAND_PADDING_PX,
            width: PLAN_NODE_WIDTH,
            height: PLAN_NODE_HEIGHT,
          });
        }
        maxCardWidth = Math.max(maxCardWidth, layout.width + 2 * BAND_PADDING_PX);
      }

      y = top + BAND_HEADER_PX + bodyHeight + BAND_GAP_PX;
      placedBands.push({ band, top, bodyHeight, expanded, hasCycle, unscheduled, outsideWindow });
    }

    return {
      placedBands,
      rectsById,
      width: isTimeline ? timelineWidth : Math.max(maxCardWidth, 480),
      height: Math.max(y, 0),
    };
  }, [bands, expandedIds, view, columns.length, viewStartMs, viewEndMs, containerWidth]);

  // Only edges with both ends visible are drawable; the rest surface as counts.
  const drawableEdges = useMemo(
    () => (data?.edges ?? []).filter(
      (e) => geometry.rectsById.has(e.predecessorRequestId) && geometry.rectsById.has(e.successorRequestId),
    ),
    [data?.edges, geometry.rectsById],
  );

  // Per node: edges whose other end is hidden (a collapsed band; in timeline mode also undated
  // or off-window work) join the server's out-of-site counts, so a task never silently looks
  // link-free just because its neighbour cannot be drawn.
  const hiddenCounts = useMemo(() => {
    const preds = new Map<string, number>();
    const succs = new Map<string, number>();
    for (const e of data?.edges ?? []) {
      const fromVisible = geometry.rectsById.has(e.predecessorRequestId);
      const toVisible = geometry.rectsById.has(e.successorRequestId);
      if (toVisible && !fromVisible)
        preds.set(e.successorRequestId, (preds.get(e.successorRequestId) ?? 0) + 1);
      if (fromVisible && !toVisible)
        succs.set(e.predecessorRequestId, (succs.get(e.predecessorRequestId) ?? 0) + 1);
    }
    return { preds, succs };
  }, [data?.edges, geometry.rectsById]);

  // The condition badge is judged against ALL predecessors, drawn or not.
  const predecessorCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const child of data?.children ?? []) counts.set(child.id, child.externalPredecessorCount);
    for (const edge of data?.edges ?? [])
      counts.set(edge.successorRequestId, (counts.get(edge.successorRequestId) ?? 0) + 1);
    return counts;
  }, [data?.children, data?.edges]);

  // Cross-links per band while collapsed, so a folded contract still says it is entangled.
  const bandEdgeCounts = useMemo(() => {
    const memberOf = new Map<string, string>();
    for (const band of bands) for (const c of band.children) memberOf.set(c.id, band.id);
    const counts = new Map<string, number>();
    for (const e of data?.edges ?? []) {
      const a = memberOf.get(e.predecessorRequestId);
      const b = memberOf.get(e.successorRequestId);
      if (a && b && a !== b) {
        counts.set(a, (counts.get(a) ?? 0) + 1);
        counts.set(b, (counts.get(b) ?? 0) + 1);
      }
    }
    return counts;
  }, [bands, data?.edges]);

  const toggleBand = useCallback((id: string) => {
    setExpandedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  // Structure-view zoom about the viewport centre — the same deliberate scroll correction the
  // per-group planner applies, copied rather than shared while there are exactly two consumers.
  // Timeline view has no card zoom: the scale selector IS its zoom.
  const zoomAnchor = useRef<{ x: number; y: number } | null>(null);
  const applyZoom = useCallback((next: number) => {
    const el = scrollRef.current;
    if (el) {
      zoomAnchor.current = {
        x: (el.scrollLeft + el.clientWidth / 2) / zoom,
        y: (el.scrollTop + el.clientHeight / 2) / zoom,
      };
    }
    setZoom(next);
  }, [zoom]);
  useLayoutEffect(() => {
    const el = scrollRef.current;
    const anchor = zoomAnchor.current;
    zoomAnchor.current = null;
    if (!el || !anchor) return;
    el.scrollLeft = anchor.x * zoom - el.clientWidth / 2;
    el.scrollTop = anchor.y * zoom - el.clientHeight / 2;
  }, [zoom]);
  if (isLoading) return <LoadingSpinner fullScreen={false} message="Loading the plan…" />;
  if (error || !data) return <ErrorAlert message="Could not load the site's plan." />;

  if (isPhone) {
    // Same reasoning as the per-group planner: 190px cards on a canvas wider than any phone.
    return (
      <p className="p-6 text-sm text-muted-foreground">
        The plan view needs a larger screen. Open a task and use its Dependencies tab to see what
        it waits for.
      </p>
    );
  }

  if (bands.length === 0) {
    return (
      <p className="p-6 text-sm text-muted-foreground">
        No tasks at this site yet. Create requests on the Requests page and they appear here with
        their dependencies.
      </p>
    );
  }

  const nowPct = viewPositionPercent(nowMs, viewStartMs, viewEndMs);

  // Rendered identically by both view paths below; only the surface around it differs.
  const bandsContent = geometry.placedBands.map(
    ({ band, top, bodyHeight, expanded, hasCycle, unscheduled, outsideWindow }) => (
      <div key={band.id} className="absolute left-0 right-0" style={{ top }}>
        {/* Flat and contiguous, like the grids' group rows: the border-b is the separator. */}
        <div
          className="flex items-center gap-2 border-b bg-muted/30 px-2 hover:bg-muted/50"
          style={{ height: BAND_HEADER_PX, width: geometry.width }}
        >
          <button
            type="button"
            onClick={() => toggleBand(band.id)}
            aria-expanded={expanded}
            className="flex min-w-0 flex-1 items-center gap-1.5 text-left"
          >
            <ChevronRight
              className={`h-4 w-4 shrink-0 text-muted-foreground transition-transform ${expanded ? "rotate-90" : ""}`}
              aria-hidden="true"
            />
            <span className="truncate text-sm font-medium">{band.name}</span>
            <span className="text-xs text-muted-foreground">
              {band.children.length} task{band.children.length === 1 ? "" : "s"}
            </span>
            {view === "timeline" && unscheduled > 0 && (
              <span className="text-xs text-muted-foreground" title="Tasks without dates have no place on a time axis">
                · {unscheduled} unscheduled
              </span>
            )}
            {view === "timeline" && expanded && outsideWindow > 0 && (
              <span className="text-xs text-muted-foreground">
                · {outsideWindow} outside this period
              </span>
            )}
            {!expanded && (bandEdgeCounts.get(band.id) ?? 0) > 0 && (
              <span className="text-xs text-muted-foreground" title="Dependencies crossing this group">
                · {bandEdgeCounts.get(band.id)} cross-group
              </span>
            )}
          </button>
          {band.id !== UNGROUPED_ID && (
            <Button
              variant="ghost"
              size="sm"
              className="shrink-0"
              onClick={() => onOpenGroupPlanner(band.id)}
            >
              <Network className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
              Sequence
            </Button>
          )}
        </div>

        {expanded && (
          <div className="relative" style={{ height: bodyHeight }}>
            {/* Body cells only — the grids tint headers but never hatch them, and stripes
                spanning the whole surface were what bled across headers and gaps. */}
            {view === "timeline" && (
              <ColumnUnderlay columns={columns} width={geometry.width} />
            )}
            {/* Overlaid, not flowed: band heights are precomputed, and a flowed warning
                would push every later band out from under its edges. */}
            {hasCycle && (
              <p className="absolute left-2 top-1 z-10 text-xs text-destructive">
                These tasks depend on each other in a loop and cannot be ordered.
              </p>
            )}
            {band.children.map((child) => {
              const rect = geometry.rectsById.get(child.id);
              if (!rect) return null;
              // Rects are absolute on the canvas; this container starts at the band top.
              const relY = rect.y - top - BAND_HEADER_PX;
              if (view === "timeline") {
                return (
                  <TimelineTaskBar
                    key={child.id}
                    child={child}
                    x={rect.x}
                    y={relY}
                    width={rect.width}
                    severity={getEventConflictSeverity(child.id, conflictsByRequest)}
                    selected={selectedNodeId === child.id}
                    onOpen={onOpenRequest}
                    onSelect={setSelectedNodeId}
                  />
                );
              }
              const hiddenPreds = hiddenCounts.preds.get(child.id) ?? 0;
              const hiddenSuccs = hiddenCounts.succs.get(child.id) ?? 0;
              return (
                <PlanNodeCard
                  key={child.id}
                  child={{
                    ...child,
                    externalPredecessorCount: child.externalPredecessorCount + hiddenPreds,
                    externalSuccessorCount: child.externalSuccessorCount + hiddenSuccs,
                  }}
                  x={rect.x}
                  y={relY}
                  predecessorCount={predecessorCounts.get(child.id) ?? 0}
                  selected={selectedNodeId === child.id}
                  editable={false}
                  onOpen={onOpenRequest}
                  onSelect={setSelectedNodeId}
                />
              );
            })}
          </div>
        )}
      </div>
    ),
  );

  return (
    <div className="flex h-full min-h-0 flex-col" data-testid="site-plan-canvas">
      {/* Key on the left, controls on the right — the arrangement every other tab uses. */}
      <div className="flex items-center gap-2 border-b px-3 py-2 text-xs text-muted-foreground">
        {view === "timeline" && (
          <div className="hidden flex-wrap items-center gap-x-3 gap-y-1 lg:flex" aria-hidden="true">
            {REQUEST_LEGEND.map((item) => (
              <span key={item.label} className="flex items-center gap-1">
                <span className={`inline-block h-2.5 w-4 rounded-sm border ${item.className}`} />
                {item.label}
              </span>
            ))}
          </div>
        )}
        <span className="ml-auto">
          {data.children.length} task{data.children.length === 1 ? "" : "s"} in{" "}
          {bands.length} group{bands.length === 1 ? "" : "s"} · {data.edges.length} dependenc
          {data.edges.length === 1 ? "y" : "ies"}
        </span>
        {view === "structure" && (
          <div className="flex items-center gap-1">
            <Button
              variant="outline" size="icon" aria-label="Zoom out"
              onClick={() => applyZoom(Math.max(ZOOM_MIN, zoom - ZOOM_STEP))}
              disabled={zoom <= ZOOM_MIN}
            >
              <ZoomOut className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" aria-label="Reset zoom" onClick={() => applyZoom(1)}>
              <Maximize className="h-4 w-4" />
            </Button>
            <Button
              variant="outline" size="icon" aria-label="Zoom in"
              onClick={() => applyZoom(Math.min(ZOOM_MAX, zoom + ZOOM_STEP))}
              disabled={zoom >= ZOOM_MAX}
            >
              <ZoomIn className="h-4 w-4" />
            </Button>
          </div>
        )}
      </div>

      {/* Two strata, like the grids: a flush sticky date header, then the scrolling body.
          Structure view keeps its padded, scale-transformed surface — `position: sticky`
          cannot survive under a CSS transform, which is what splits these paths. */}
      {view === "timeline" ? (
        <div ref={attachScroller} className="min-h-0 flex-1 overflow-auto">
          <div className="relative" style={{ width: geometry.width }}>
            <div
              className="sticky top-0 z-30 flex bg-background"
              style={{ height: TIMELINE_HEADER_PX }}
            >
              {columns.map((col) => (
                <div
                  key={col.start.getTime()}
                  className={`flex items-center justify-center border-b border-r text-center text-xs font-medium text-muted-foreground ${columnHeaderTintClass(col)}`}
                  style={{ width: geometry.width / columns.length }}
                  title={columnHeaderTitle(col, scale)}
                >
                  {col.label}
                </div>
              ))}
            </div>

            <div className="relative" style={{ height: geometry.height }}>
              <PlanEdgeLayer
                edges={drawableEdges}
                rectsById={geometry.rectsById}
                width={geometry.width}
                height={geometry.height}
                selectedEdgeId={null}
                violatingEdgeIds={violatingEdgeIds}
              />
              {bandsContent}
              {/* Below the header, like NowLine on the grids — the pill can no longer
                  collide with the date row. */}
              {nowPct !== null && (
                <div
                  data-testid="site-plan-now"
                  aria-hidden="true"
                  className="pointer-events-none absolute top-0 bottom-0 z-20 w-0.5 bg-rose-500"
                  style={{ left: `${nowPct}%` }}
                >
                  <span className="absolute top-0 left-1/2 -translate-x-1/2 rounded-sm bg-rose-500 px-1 text-[10px] font-medium leading-tight text-white">
                    Now
                  </span>
                </div>
              )}
            </div>
          </div>
        </div>
      ) : (
        <div ref={attachScroller} className="min-h-0 flex-1 overflow-auto p-4">
          <div
            className="relative"
            style={{
              width: geometry.width * zoom + CANVAS_PADDING,
              height: geometry.height * zoom + CANVAS_PADDING,
            }}
          >
            <div
              className="absolute left-0 top-0 origin-top-left"
              style={{ transform: `scale(${zoom})`, width: geometry.width, height: geometry.height }}
            >
              <PlanEdgeLayer
                edges={drawableEdges}
                rectsById={geometry.rectsById}
                width={geometry.width}
                height={geometry.height}
                selectedEdgeId={null}
                violatingEdgeIds={violatingEdgeIds}
              />
              {bandsContent}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * The tinted / hatched time columns behind one band's bars — the body-cell treatment the
 * utilization grids give their rows, from the same `columnTintClass` source. Confined to a
 * band body on purpose: headers are tinted but never hatched.
 */
function ColumnUnderlay({ columns, width }: { columns: readonly TimeColumn[]; width: number }) {
  const colPx = width / columns.length;
  return (
    <>
      {columns.map((col, i) => {
        const tint = columnTintClass(col);
        const hatch = tint === OFFTIME_TINT_CLASS ? PROBLEM_HATCH_CLASS : "";
        return (
          <div
            key={col.start.getTime()}
            aria-hidden="true"
            className={`absolute top-0 bottom-0 border-r ${tint} ${hatch}`}
            style={{ left: i * colPx, width: colPx }}
          />
        );
      })}
    </>
  );
}

/**
 * One dated task on the timeline, coloured the way the Calendar tab colours its blocks: a
 * translucent status tint, overridden by conflict severity. These bars ARE requests, so the
 * request-status palette is the right one — the stations grid's assigned/overbooked tones
 * describe what a bar does to a station, which is not the question here. Both surfaces read
 * from the same swatch maps, so they cannot drift apart.
 *
 * The card's badge row (condition, external links) does not fit a duration-wide bar — an
 * accepted limitation; the structure view carries the badges.
 */
function TimelineTaskBar({
  child,
  x,
  y,
  width,
  severity,
  selected,
  onOpen,
  onSelect,
}: {
  child: RequestPlanChild;
  x: number;
  y: number;
  width: number;
  /** From the conflict registry: error paints red, warning amber, null lets the status show. */
  severity: ConflictSeverity;
  selected: boolean;
  onOpen: (requestId: string) => void;
  onSelect: (requestId: string) => void;
}) {
  const tone = severity ? SEVERITY_SWATCH[severity] : STATUS_SWATCH[child.status];
  const text = child.status === "cancelled" || child.status === "deferred"
    ? "text-muted-foreground line-through"
    : "text-foreground";
  const stateWord = severity === "error" ? ", has conflicts" : severity === "warning" ? ", has warnings" : "";
  return (
    <div
      role="button"
      tabIndex={0}
      data-testid={`plan-bar-${child.id}`}
      className={`absolute rounded border p-1 text-xs ${tone} ${text} overflow-hidden cursor-pointer
        transition motion-reduce:transition-none hover:brightness-95
        focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
        ${selected ? "ring-2 ring-primary/60" : ""}`}
      style={{ left: x, top: y, width, height: BAR_HEIGHT_PX }}
      title={child.name}
      // The colour is not the only carrier (WCAG 1.4.1): status and conflict state are named.
      aria-label={`${child.name}, ${child.status.replace("_", " ")}${stateWord}. Open request.`}
      onClick={() => onSelect(child.id)}
      onDoubleClick={() => onOpen(child.id)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onOpen(child.id);
        }
      }}
    >
      <RequestBarLabel request={child} hasConflict={severity !== null} />
    </div>
  );
}

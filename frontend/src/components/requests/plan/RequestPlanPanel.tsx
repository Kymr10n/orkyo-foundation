import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ZoomIn, ZoomOut, Maximize } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { Combobox } from "@foundation/src/components/ui/combobox";
import { Label } from "@foundation/src/components/ui/label";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import { getRequestPlan } from "@foundation/src/lib/api/request-plan-api";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import {
  addRequestDependency,
  deleteRequestDependency,
} from "@foundation/src/lib/api/request-dependency-api";
import {
  computePlanLayout,
  splitPlanChildren,
  wouldCreateCycle,
  PLAN_NODE_HEIGHT,
  PLAN_NODE_WIDTH,
} from "@foundation/src/domain/plan-layout";
import { PlanEdgeLayer } from "./PlanEdgeLayer";
import { PlanNodeCard } from "./PlanNodeCard";
import { PlanBacklogTray } from "./PlanBacklogTray";

const ZOOM_MIN = 0.5;
const ZOOM_MAX = 2;
const ZOOM_STEP = 0.25;
const CANVAS_PADDING = 32;
/** How long a just-linked pair stays ringed. Long enough to find, short enough not to linger. */
const HIGHLIGHT_MS = 1600;

/**
 * The dependency planner: one parent's children as nodes, the dependencies among them as edges.
 *
 * A separate surface from the request tree on purpose. The tree's drag gesture means "reparent",
 * and overloading it to also mean "sequence" would make every mis-drop ambiguous — containment
 * and precedence are different questions. Here drag means only "sequence".
 *
 * Pan/zoom and the press-versus-click threshold follow SpaceDrawingCanvas, the floorplan editor;
 * the mechanics are deliberately copied rather than shared, because two consumers is not yet a
 * reason to couple the two editors to one abstraction.
 */
export function RequestPlanPanel({
  requestId,
  onOpenRequest,
}: {
  requestId: string;
  onOpenRequest?: (requestId: string) => void;
}) {
  const canEdit = useCanEdit();
  const { isPhone } = useBreakpoint();
  const [zoom, setZoom] = useState(1);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null);
  const [edgeToRemove, setEdgeToRemove] = useState<string | null>(null);
  // `direction` records which port started the drag, so the edge is written the way the user
  // meant rather than inferred from where they let go.
  const [connecting, setConnecting] = useState<
    { anchorId: string; direction: "after" | "before"; x: number; y: number } | null
  >(null);
  const [edgeError, setEdgeError] = useState<string | null>(null);
  // Tasks the user pulled out of the tray. They have no edges yet, so nothing in the plan says
  // they belong on the canvas — but the user has said so, and taking them back the moment they
  // change their mind would fight them.
  const [staged, setStaged] = useState<ReadonlySet<string>>(() => new Set());
  // The pair a link was just made between, so the canvas can point at what changed.
  const [justLinked, setJustLinked] = useState<{ from: string; to: string } | null>(null);

  const surfaceRef = useRef<HTMLDivElement | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: qk.requests.plan(requestId),
    queryFn: () => getRequestPlan(requestId),
    staleTime: STALE.OPERATIONAL,
  });

  // Edges the conflict engine says this plan violates, so the drawing agrees with the Conflicts
  // page instead of showing a tidy graph over work the server has already flagged.
  const { conflictsByRequest } = useConflictRegistry();
  const violatingEdgeIds = useMemo(() => {
    const ids = new Set<string>();
    for (const edge of data?.edges ?? []) {
      const conflicts = conflictsByRequest.get(edge.successorRequestId) ?? [];
      // A join-condition shortfall names no peer — it is a property of the whole incoming set —
      // so it marks every edge into that successor.
      if (conflicts.some((c) =>
        c.kind === "dependency_violation"
        && (c.peerRequestId === edge.predecessorRequestId || !c.peerRequestId)
      )) ids.add(edge.id);
    }
    return ids;
  }, [data?.edges, conflictsByRequest]);

  // The canvas draws what is sequenced plus what the user has staged; the tray holds the rest.
  const { sequenced, unsequenced } = useMemo(
    () => splitPlanChildren(data?.children ?? [], data?.edges ?? []),
    [data?.children, data?.edges],
  );
  const canvasChildren = useMemo(
    () => [...sequenced, ...unsequenced.filter((c) => staged.has(c.id))]
      .sort((a, b) => a.sortOrder - b.sortOrder),
    [sequenced, unsequenced, staged],
  );
  const trayChildren = useMemo(
    () => unsequenced.filter((c) => !staged.has(c.id)),
    [unsequenced, staged],
  );

  const layout = useMemo(
    () => computePlanLayout(canvasChildren, data?.edges ?? []),
    [canvasChildren, data?.edges],
  );

  // Stable identity: a fresh closure here would defeat PlanNodeCard's memo, so every pointer
  // move during a connect would re-render every card in the plan.
  const handleOpenRequest = useCallback((id: string) => onOpenRequest?.(id), [onOpenRequest]);


  const nodesById = useMemo(
    () => new Map(layout.nodes.map((n) => [n.id, n])),
    [layout.nodes],
  );

  // How many predecessors each child has, for the condition badge — including the ones outside
  // the group, because the condition is judged against all of them.
  const predecessorCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const child of data?.children ?? []) counts.set(child.id, child.externalPredecessorCount);
    for (const edge of data?.edges ?? [])
      counts.set(edge.successorRequestId, (counts.get(edge.successorRequestId) ?? 0) + 1);
    return counts;
  }, [data?.children, data?.edges]);

  const linkMutation = useMutation({
    mutationFn: ({ from, to }: { from: string; to: string }) => addRequestDependency(to, from),
    meta: {
      successMessage: "Dependency added",
      errorMessage: "Could not add the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: (_result, variables) => setJustLinked(variables),
  });

  const unlinkMutation = useMutation({
    mutationFn: (edgeId: string) => {
      // The plan can refetch between selecting an edge and confirming its removal — any request
      // mutation invalidates it. An edge that is already gone is the outcome the user asked for,
      // not a failure to report.
      const edge = edgesRef.current?.find((e) => e.id === edgeId);
      if (!edge) return Promise.resolve();
      return deleteRequestDependency(edge.successorRequestId, edgeId);
    },
    meta: {
      successMessage: "Dependency removed",
      errorMessage: "Could not remove the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: () => setSelectedEdgeId(null),
  });

  const selectedNode = data?.children.find((c) => c.id === selectedNodeId) ?? null;
  const otherChildren = (data?.children ?? []).filter((c) => {
    if (!selectedNode || c.id === selectedNode.id) return false;
    // Already a predecessor, or would close a loop — the same two refusals the drag applies,
    // applied before the option is offered rather than after it is chosen.
    if ((data?.edges ?? []).some(
      (e) => e.predecessorRequestId === c.id && e.successorRequestId === selectedNode.id,
    )) return false;
    return !wouldCreateCycle(data?.edges ?? [], c.id, selectedNode.id);
  });

  const linkFromSelection = useCallback((predecessorId: string, successorId: string) => {
    setEdgeError(null);
    linkMutation.mutate({ from: predecessorId, to: successorId });
  }, [linkMutation]);

  /** A tray row: onto the canvas, and selected — the two things the click is asking for. */
  const handleSelectFromTray = useCallback((id: string) => {
    setStaged((current) => new Set(current).add(id));
    setSelectedNodeId(id);
  }, []);

  // ── Keeping the change in view ────────────────────────────────────────────
  // Zoom scales about the top-left corner, so without this the viewport lands somewhere else
  // entirely on a tall plan; and a new link relayers the graph under a scroll position that
  // still points at the old shape. Both are answered by moving the scroll offset deliberately.
  const zoomAnchor = useRef<{ x: number; y: number } | null>(null);

  const applyZoom = useCallback((next: number) => {
    const el = scrollRef.current;
    // Layout-space point currently at the middle of the viewport, to be put back there after.
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

  // Bring the successor of a new link into view once the refetched plan has placed it, then let
  // the ring fade. Without this the task can land past the bottom of a plan the user is scrolled
  // into the middle of, and the link reads as having done nothing at all.
  useEffect(() => {
    if (!justLinked) return;
    const el = scrollRef.current;
    const node = nodesById.get(justLinked.to);
    if (el && node && typeof el.scrollTo === "function") {
      const reduceMotion = typeof window !== "undefined"
        && typeof window.matchMedia === "function"
        && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      el.scrollTo({
        left: (node.x + PLAN_NODE_WIDTH / 2) * zoom - el.clientWidth / 2,
        top: (node.y + PLAN_NODE_HEIGHT / 2) * zoom - el.clientHeight / 2,
        behavior: reduceMotion ? "auto" : "smooth",
      });
    }
    const timer = setTimeout(() => setJustLinked(null), HIGHLIGHT_MS);
    return () => clearTimeout(timer);
  }, [justLinked, nodesById, zoom]);

  // ── Drawing an edge ───────────────────────────────────────────────────────
  // Document-level listeners for the whole gesture, registered synchronously on pointer-down —
  // the same recipe useResizeGesture uses, and for the same reasons. Two of them matter here:
  //
  //  * Touch and pen IMPLICITLY CAPTURE the pointer to the element that received pointerdown, so
  //    every later event is dispatched at the port button no matter where the finger is. Reading
  //    `event.target` therefore always names the origin node, and the drop silently resolved to
  //    "dropped on myself" on every touch device. The drop is resolved from COORDINATES instead.
  //  * The pointer leaving the canvas no longer cancels the gesture, so two nodes that are not
  //    on screen together can still be connected by scrolling mid-drag.
  const toLayoutPoint = useCallback((clientX: number, clientY: number) => {
    const rect = surfaceRef.current?.getBoundingClientRect();
    if (!rect) return { x: 0, y: 0 };
    return { x: (clientX - rect.left) / zoom, y: (clientY - rect.top) / zoom };
  }, [zoom]);

  // Refs so the document handlers registered at pointer-down never read a stale layout or edge
  // list, without having to re-register on every render. Synced from an effect, not during
  // render — the same shape useResizeGesture uses for its callbacks.
  const layoutRef = useRef(layout);
  const edgesRef = useRef(data?.edges);
  useEffect(() => {
    layoutRef.current = layout;
    edgesRef.current = data?.edges;
  });

  /** The node under a layout-space point, or null. Geometry, not the DOM: no capture to fight. */
  const nodeAt = useCallback((x: number, y: number) => {
    const hit = layoutRef.current.nodes.find(
      (n) => x >= n.x && x <= n.x + PLAN_NODE_WIDTH && y >= n.y && y <= n.y + PLAN_NODE_HEIGHT,
    );
    return hit?.id ?? null;
  }, []);

  const finishConnect = useCallback((
    anchorId: string,
    direction: "after" | "before",
    clientX: number,
    clientY: number,
  ) => {
    setConnecting(null);

    const point = toLayoutPoint(clientX, clientY);
    const droppedId = nodeAt(point.x, point.y);
    if (!droppedId || droppedId === anchorId) return;

    // The port decides the direction: from the right port the anchor runs first, from the left
    // port it runs second.
    const [predecessorId, successorId] = direction === "after"
      ? [anchorId, droppedId]
      : [droppedId, anchorId];

    // Refuse a loop we can see rather than letting the server 409. This only knows the edges
    // INSIDE this group, so a loop closed through a request outside it still comes back as a
    // 409 — the server stays the authority.
    if (wouldCreateCycle(edgesRef.current ?? [], predecessorId, successorId)) {
      setEdgeError("That would make the plan circular.");
      return;
    }
    if ((edgesRef.current ?? []).some(
      (e) => e.predecessorRequestId === predecessorId && e.successorRequestId === successorId,
    )) return;

    linkMutation.mutate({ from: predecessorId, to: successorId });
  }, [toLayoutPoint, nodeAt, linkMutation]);

  const handleStartConnect = useCallback((
    anchorId: string,
    direction: "after" | "before",
    event: React.PointerEvent,
  ) => {
    event.preventDefault();
    event.stopPropagation();

    // A new gesture clears the last one's complaint: leaving it up would let "circular" sit over
    // a canvas the user has since fixed.
    setEdgeError(null);

    const point = toLayoutPoint(event.clientX, event.clientY);
    setConnecting({ anchorId, direction, x: point.x, y: point.y });

    const onMove = (e: PointerEvent) => {
      const p = toLayoutPoint(e.clientX, e.clientY);
      setConnecting((current) => (current ? { ...current, x: p.x, y: p.y } : current));
    };
    const onUp = (e: PointerEvent) => {
      teardown();
      finishConnect(anchorId, direction, e.clientX, e.clientY);
    };
    // pointercancel: the system took the gesture (a touch interruption, a browser scroll
    // takeover). Without this the ghost line hangs off a node and follows nothing.
    const onCancel = () => {
      teardown();
      setConnecting(null);
    };
    function teardown() {
      document.removeEventListener("pointermove", onMove);
      document.removeEventListener("pointerup", onUp);
      document.removeEventListener("pointercancel", onCancel);
    }

    document.addEventListener("pointermove", onMove);
    document.addEventListener("pointerup", onUp);
    document.addEventListener("pointercancel", onCancel);
  }, [toLayoutPoint, finishConnect]);

  if (isLoading) return <LoadingSpinner fullScreen={false} message="Loading plan…" />;
  if (error || !data) return <ErrorAlert message="Could not load this request's plan." />;

  if (isPhone) {
    // The graph is a pointer surface: 190px cards, drag-to-connect, and a canvas wider than any
    // phone. Offering it here would be offering something nobody can drive. Sequencing stays
    // available on each task's Dependencies tab, which is a list and works anywhere.
    return (
      <p className="p-6 text-sm text-muted-foreground">
        The plan view needs a larger screen. Open a task and use its Dependencies tab to set what
        it waits for.
      </p>
    );
  }

  if (data.children.length === 0) {
    return (
      <p className="p-6 text-sm text-muted-foreground">
        {data.parentName} has no children yet. Add some on the request's Children tab, then
        sequence them here.
      </p>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-2">
        <h2 className="text-sm font-medium">{data.parentName}</h2>
        <span className="text-xs text-muted-foreground">
          {data.children.length} task{data.children.length === 1 ? "" : "s"}
        </span>

        <div className="ml-auto flex items-center gap-1">
          <Button
            variant="outline" size="icon" aria-label="Zoom out"
            onClick={() => applyZoom(Math.max(ZOOM_MIN, zoom - ZOOM_STEP))}
            disabled={zoom <= ZOOM_MIN}
          >
            <ZoomOut className="h-4 w-4" />
          </Button>
          <Button
            variant="outline" size="icon" aria-label="Reset zoom"
            onClick={() => applyZoom(1)}
          >
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
      </div>

      {layout.hasCycle && (
        <div className="border-b px-3 py-2">
          <ErrorAlert message="These tasks depend on each other in a loop, so they cannot be ordered. Remove one of the dependencies to fix the plan." />
        </div>
      )}
      {edgeError && (
        <div className="border-b px-3 py-2">
          <ErrorAlert message={edgeError} />
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {/* One scroll owner for the surface; the page owns none of it. */}
        <div ref={scrollRef} className="min-w-0 flex-1 overflow-auto p-4">
          {canvasChildren.length === 0 && (
            <p className="p-6 text-sm text-muted-foreground">
              Nothing is sequenced yet. Pick a task from the list to put it on the plan, then say
              what it waits for.
            </p>
          )}
          <div
            ref={surfaceRef}
            className="relative"
            style={{
              width: layout.width * zoom + CANVAS_PADDING,
              height: layout.height * zoom + CANVAS_PADDING,
            }}
          >
            <div
              className="absolute left-0 top-0 origin-top-left"
              style={{ transform: `scale(${zoom})`, width: layout.width, height: layout.height }}
            >
              <PlanEdgeLayer
                edges={data.edges}
                nodesById={nodesById}
                width={layout.width}
                height={layout.height}
                selectedEdgeId={selectedEdgeId}
                violatingEdgeIds={violatingEdgeIds}
                onSelectEdge={canEdit ? setSelectedEdgeId : undefined}
                pendingEdge={
                  connecting && nodesById.has(connecting.anchorId)
                    ? {
                        from: {
                          x: nodesById.get(connecting.anchorId)!.x
                            + (connecting.direction === "after" ? PLAN_NODE_WIDTH : 0),
                          y: nodesById.get(connecting.anchorId)!.y + PLAN_NODE_HEIGHT / 2,
                        },
                        to: { x: connecting.x, y: connecting.y },
                      }
                    : null
                }
              />

              {canvasChildren.map((child) => {
                const node = nodesById.get(child.id);
                if (!node) return null;
                return (
                  <PlanNodeCard
                    key={child.id}
                    child={child}
                    x={node.x}
                    y={node.y}
                    predecessorCount={predecessorCounts.get(child.id) ?? 0}
                    selected={child.id === selectedNodeId}
                    highlighted={
                      justLinked?.from === child.id || justLinked?.to === child.id
                    }
                    editable={canEdit}
                    onSelect={setSelectedNodeId}
                    onOpen={handleOpenRequest}
                    onStartConnect={handleStartConnect}
                  />
                );
              })}
            </div>
          </div>
        </div>

        {trayChildren.length > 0 && (
          <PlanBacklogTray
            tasks={trayChildren}
            selectedId={selectedNodeId}
            onSelect={handleSelectFromTray}
          />
        )}
      </div>

      {/* The selection bar is the keyboard route to everything the canvas offers by pointer:
          opening a task, and linking two of them — including two that are nowhere near each
          other, which no drag can reach on a plan this wide. */}
      {selectedNode && (
        <div className="flex flex-wrap items-center gap-2 border-t px-3 py-2">
          <span className="truncate text-xs font-medium">{selectedNode.name}</span>
          <Button variant="outline" size="sm" onClick={() => handleOpenRequest(selectedNode.id)}>
            Open task
          </Button>

          {canEdit && otherChildren.length > 0 && (
            <div className="ml-auto flex items-center gap-2">
              <Label htmlFor="plan-add-predecessor" className="text-xs whitespace-nowrap">
                Wait for
              </Label>
              <Combobox
                id="plan-add-predecessor"
                className="w-[220px]"
                value=""
                placeholder="Add a predecessor…"
                options={otherChildren.map((c) => ({ id: c.id, label: c.name }))}
                onChange={(predecessorId) => {
                  if (!predecessorId) return;
                  linkFromSelection(predecessorId, selectedNode.id);
                }}
              />
            </div>
          )}
        </div>
      )}

      {canEdit && selectedEdgeId && (
        <div className="flex items-center gap-2 border-t px-3 py-2">
          <span className="text-xs text-muted-foreground">Dependency selected</span>
          <Button
            variant="outline" size="sm" className="ml-auto"
            onClick={() => setEdgeToRemove(selectedEdgeId)}
          >
            Remove
          </Button>
        </div>
      )}

      <ConfirmDialog
        open={edgeToRemove !== null}
        onOpenChange={(open) => !open && setEdgeToRemove(null)}
        title="Remove this dependency?"
        description="The two tasks stay where they are; only the ordering between them goes."
        confirmLabel="Remove"
        onConfirm={() => {
          if (edgeToRemove) unlinkMutation.mutate(edgeToRemove);
          setEdgeToRemove(null);
        }}
      />
    </div>
  );
}

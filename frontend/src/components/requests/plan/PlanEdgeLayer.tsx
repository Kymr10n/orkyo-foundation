import { memo } from "react";
import type { RequestDependency } from "@foundation/src/lib/api/request-dependency-api";
import type { PlanLayoutNode } from "@foundation/src/domain/plan-layout";
import { PLAN_NODE_HEIGHT, PLAN_NODE_WIDTH } from "@foundation/src/domain/plan-layout";

/** Where an edge leaves a node (its right side) and where it arrives (the next node's left). */
function exitPoint(node: PlanLayoutNode) {
  return { x: node.x + PLAN_NODE_WIDTH, y: node.y + PLAN_NODE_HEIGHT / 2 };
}
function entryPoint(node: PlanLayoutNode) {
  return { x: node.x, y: node.y + PLAN_NODE_HEIGHT / 2 };
}

/**
 * A cubic bezier whose control points are pulled horizontally, so an edge leaves a node
 * rightwards and arrives leftwards no matter how the rows line up. A straight line between
 * distant rows would cut across the nodes between them.
 */
export function edgePath(from: { x: number; y: number }, to: { x: number; y: number }): string {
  const pull = Math.max(40, Math.abs(to.x - from.x) / 2);
  return `M ${from.x} ${from.y} C ${from.x + pull} ${from.y}, ${to.x - pull} ${to.y}, ${to.x} ${to.y}`;
}

const ARROWHEAD_ID = "plan-edge-arrowhead";

/**
 * The edges of a plan, under the node layer.
 *
 * SVG rather than HTML because these are curves; the nodes above are HTML so they keep real
 * buttons and native focus behaviour, which foreignObject would put at risk on the oldest
 * browser this product supports.
 */
export const PlanEdgeLayer = memo(function PlanEdgeLayer({
  edges,
  nodesById,
  width,
  height,
  selectedEdgeId,
  violatingEdgeIds,
  onSelectEdge,
  pendingEdge,
}: {
  edges: readonly RequestDependency[];
  nodesById: ReadonlyMap<string, PlanLayoutNode>;
  width: number;
  height: number;
  selectedEdgeId: string | null;
  /** Edges the conflict engine says this plan violates — drawn in the error colour. */
  violatingEdgeIds: ReadonlySet<string>;
  onSelectEdge?: (edgeId: string) => void;
  /** The edge being dragged out of a port, in layout coordinates. */
  pendingEdge?: { from: { x: number; y: number }; to: { x: number; y: number } } | null;
}) {
  return (
    <svg
      className="absolute left-0 top-0 overflow-visible"
      width={width}
      height={height}
      aria-hidden="true"
    >
      <defs>
        <marker
          id={ARROWHEAD_ID}
          viewBox="0 0 10 10"
          refX="9"
          refY="5"
          markerWidth="6"
          markerHeight="6"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" className="fill-muted-foreground" />
        </marker>
      </defs>

      {edges.map((edge) => {
        const from = nodesById.get(edge.predecessorRequestId);
        const to = nodesById.get(edge.successorRequestId);
        if (!from || !to) return null;

        const isSelected = edge.id === selectedEdgeId;
        const isViolating = violatingEdgeIds.has(edge.id);

        return (
          <g key={edge.id}>
            {/* A 2px curve is nearly impossible to hit. This invisible one gives it a real
                target without thickening the line the user sees. */}
            <path
              d={edgePath(exitPoint(from), entryPoint(to))}
              className="fill-none stroke-transparent"
              strokeWidth={14}
              style={{ cursor: onSelectEdge ? "pointer" : undefined, pointerEvents: "stroke" }}
              onPointerDown={onSelectEdge ? () => onSelectEdge(edge.id) : undefined}
            />
            <path
              d={edgePath(exitPoint(from), entryPoint(to))}
              className={`fill-none ${
                isViolating
                  ? "stroke-destructive"
                  : isSelected
                    ? "stroke-primary"
                    : "stroke-muted-foreground"
              }`}
              strokeWidth={isSelected ? 3 : 2}
              markerEnd={`url(#${ARROWHEAD_ID})`}
              style={{ pointerEvents: "none" }}
            />
          </g>
        );
      })}

      {pendingEdge && (
        <path
          d={edgePath(pendingEdge.from, pendingEdge.to)}
          className="fill-none stroke-primary"
          strokeWidth={2}
          strokeDasharray="4 4"
          style={{ pointerEvents: "none" }}
        />
      )}
    </svg>
  );
});

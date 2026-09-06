import type { RequestDependency } from "@foundation/src/lib/api/request-dependency-api";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * The edges the conflict engine says a plan violates, so the drawing agrees with the Conflicts
 * page instead of showing a tidy graph over work the server has already flagged. Shared by the
 * per-group planner and the site-wide canvas — one rule, two drawings.
 *
 * A join-condition shortfall names no peer — it is a property of the whole incoming set — so it
 * marks every edge into that successor.
 */
export function collectViolatingEdgeIds(
  edges: readonly RequestDependency[],
  conflictsByRequest: ReadonlyMap<string, Conflict[]>,
): Set<string> {
  const ids = new Set<string>();
  for (const edge of edges) {
    const conflicts = conflictsByRequest.get(edge.successorRequestId) ?? [];
    if (conflicts.some((c) =>
      c.kind === "dependency_violation"
      && (c.peerRequestId === edge.predecessorRequestId || !c.peerRequestId)
    )) ids.add(edge.id);
  }
  return ids;
}

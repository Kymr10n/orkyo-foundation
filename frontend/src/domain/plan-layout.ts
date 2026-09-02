import type { RequestPlanChild } from "@foundation/src/lib/api/request-plan-api";
import type { RequestDependency } from "@foundation/src/lib/api/request-dependency-api";

/**
 * Places a plan's tasks on a grid: predecessors to the left, successors to the right.
 *
 * Pure and separate from the canvas so the hard part — layering a graph and keeping it stable
 * as edges come and go — is testable without rendering anything.
 */

/** Grid geometry. Pixels, so the canvas can draw straight from a layout without converting. */
export const PLAN_NODE_WIDTH = 190;
export const PLAN_NODE_HEIGHT = 84;
export const PLAN_COLUMN_GAP = 90;
export const PLAN_ROW_GAP = 28;

export interface PlanLayoutNode {
  id: string;
  /** Longest-path depth from a task with no predecessors inside the group. */
  column: number;
  row: number;
  x: number;
  y: number;
}

export interface PlanLayout {
  nodes: PlanLayoutNode[];
  width: number;
  height: number;
  /** True when the internal edges contain a cycle, which no layering can express. */
  hasCycle: boolean;
}

/**
 * Splits a plan's children into the ones that take part in the ordering and the ones that do not.
 *
 * A canvas that draws every child does not scale: a 400-task group renders one column tall enough
 * to hide the handful of tasks that are actually sequenced, and the head of a chain legitimately
 * stays in that column, which reads as if linking a task did nothing. The unsequenced tasks belong
 * in a list, where 400 of them cost 400 rows and nothing else.
 *
 * A task with a dependency to a request OUTSIDE this group counts as sequenced even though no edge
 * can be drawn for it. It is not waiting for nothing — the card already says how many links leave
 * the group — and filing it under "unsequenced" would be a lie the user has to check to disbelieve.
 */
export function splitPlanChildren(
  children: readonly RequestPlanChild[],
  edges: readonly RequestDependency[],
): { sequenced: RequestPlanChild[]; unsequenced: RequestPlanChild[] } {
  const ids = new Set(children.map((c) => c.id));
  const inEdge = new Set<string>();
  for (const edge of edges) {
    if (!ids.has(edge.predecessorRequestId) || !ids.has(edge.successorRequestId)) continue;
    inEdge.add(edge.predecessorRequestId);
    inEdge.add(edge.successorRequestId);
  }

  const sequenced: RequestPlanChild[] = [];
  const unsequenced: RequestPlanChild[] = [];
  for (const child of children) {
    const external = child.externalPredecessorCount + child.externalSuccessorCount;
    (inEdge.has(child.id) || external > 0 ? sequenced : unsequenced).push(child);
  }
  return { sequenced, unsequenced };
}

/**
 * Longest-path layering (Kahn), then a barycentre pass to order rows.
 *
 * Longest path rather than shortest: a task must sit to the right of EVERY predecessor, or an
 * edge would point backwards and the drawing would contradict the plan.
 *
 * A cycle cannot be layered at all. The server refuses to create one, but a plan can still be
 * read while another session is mid-change, so the remainder is appended in a final column and
 * flagged rather than throwing — a planner that renders nothing is worse than one that renders
 * an obviously wrong shape the user can fix.
 */
export function computePlanLayout(
  children: readonly RequestPlanChild[],
  edges: readonly RequestDependency[],
): PlanLayout {
  const ids = new Set(children.map((c) => c.id));
  const internal = edges.filter(
    (e) => ids.has(e.predecessorRequestId) && ids.has(e.successorRequestId),
  );

  const incoming = new Map<string, string[]>();
  const outgoing = new Map<string, string[]>();
  const indegree = new Map<string, number>();
  for (const child of children) {
    incoming.set(child.id, []);
    outgoing.set(child.id, []);
    indegree.set(child.id, 0);
  }
  for (const edge of internal) {
    incoming.get(edge.successorRequestId)!.push(edge.predecessorRequestId);
    outgoing.get(edge.predecessorRequestId)!.push(edge.successorRequestId);
    indegree.set(edge.successorRequestId, indegree.get(edge.successorRequestId)! + 1);
  }

  // Sort order decides the starting column's order, so a plan the user has arranged in the tree
  // opens looking the way they left it.
  const bySortOrder = [...children].sort((a, b) => a.sortOrder - b.sortOrder);

  const column = new Map<string, number>();
  const queue = bySortOrder.filter((c) => indegree.get(c.id) === 0).map((c) => c.id);
  for (const id of queue) column.set(id, 0);

  let head = 0;
  while (head < queue.length) {
    const id = queue[head++];
    for (const successor of outgoing.get(id)!) {
      // Longest path: a task moves right every time a deeper predecessor is found.
      const candidate = column.get(id)! + 1;
      if (candidate > (column.get(successor) ?? -1)) column.set(successor, candidate);

      indegree.set(successor, indegree.get(successor)! - 1);
      if (indegree.get(successor) === 0) queue.push(successor);
    }
  }

  const unplaced = bySortOrder.filter((c) => !column.has(c.id));
  const hasCycle = unplaced.length > 0;
  if (hasCycle) {
    // One column each, not one column for all of them. Sharing a column gives every intra-cycle
    // edge the same x on both ends, and the curve then leaves a node's right side, doubles back
    // over the node itself and lands on its own left edge — which reads as a rendering fault
    // rather than as "these two depend on each other". Stepping them right at least draws the
    // loop as a loop.
    let next = Math.max(-1, ...column.values()) + 1;
    for (const child of unplaced) column.set(child.id, next++);
  }

  // ── Row ordering ──────────────────────────────────────────────────────────
  // Within a column, sit each task near the average row of its predecessors. One pass, left to
  // right: enough to keep most edges from crossing, and cheap enough to run on every change.
  const columns = new Map<number, string[]>();
  for (const child of bySortOrder) {
    const col = column.get(child.id)!;
    if (!columns.has(col)) columns.set(col, []);
    columns.get(col)!.push(child.id);
  }

  const row = new Map<string, number>();
  for (const col of [...columns.keys()].sort((a, b) => a - b)) {
    const members = columns.get(col)!;
    const ordered = col === 0
      ? members
      : [...members].sort((a, b) => barycentre(a, incoming, row) - barycentre(b, incoming, row));

    // Position, not just order. Packing every column densely from row 0 puts a task whose only
    // predecessor sits at row 199 up at row 0, and the edge between them then sweeps vertically
    // across two hundred cards. Each task is placed at its predecessors' average row where that
    // row is still free, so a chain stays roughly level with what feeds it; ties and gaps fall
    // through to the next free row, which keeps the dense packing when nothing pulls.
    const taken = new Set<number>();
    ordered.forEach((id, index) => {
      const preferred = col === 0 ? index : Math.round(barycentreOr(id, incoming, row, index));
      let candidate = Math.max(0, preferred);
      while (taken.has(candidate)) candidate++;
      taken.add(candidate);
      row.set(id, candidate);
    });
    columns.set(col, ordered);
  }

  const nodes = bySortOrder.map((child) => {
    const col = column.get(child.id)!;
    const r = row.get(child.id)!;
    return {
      id: child.id,
      column: col,
      row: r,
      x: col * (PLAN_NODE_WIDTH + PLAN_COLUMN_GAP),
      y: r * (PLAN_NODE_HEIGHT + PLAN_ROW_GAP),
    };
  });

  const columnCount = columns.size;
  // Rows are now positions, not indices, so the extent is the largest row in use plus one.
  const tallest = row.size === 0 ? 0 : Math.max(...row.values()) + 1;

  return {
    nodes,
    width: columnCount === 0 ? 0 : columnCount * PLAN_NODE_WIDTH + (columnCount - 1) * PLAN_COLUMN_GAP,
    height: tallest === 0 ? 0 : tallest * PLAN_NODE_HEIGHT + (tallest - 1) * PLAN_ROW_GAP,
    hasCycle,
  };
}

/** Average row of the predecessors placed so far; unplaced ones leave a task where it is. */
function barycentre(
  id: string,
  incoming: Map<string, string[]>,
  row: Map<string, number>,
): number {
  const rows = (incoming.get(id) ?? []).map((p) => row.get(p)).filter((r): r is number => r !== undefined);
  if (rows.length === 0) return Number.MAX_SAFE_INTEGER;
  return rows.reduce((sum, r) => sum + r, 0) / rows.length;
}

/** The same average, but falling back to a concrete row rather than "sort me last". */
function barycentreOr(
  id: string,
  incoming: Map<string, string[]>,
  row: Map<string, number>,
  fallback: number,
): number {
  const value = barycentre(id, incoming, row);
  return value === Number.MAX_SAFE_INTEGER ? fallback : value;
}

/**
 * Whether adding `predecessor → successor` would close a loop, so the editor can refuse the
 * gesture rather than let the user draw an edge the server will reject with a 409.
 *
 * The server stays authoritative — this only spares a round trip and a confusing toast.
 */
export function wouldCreateCycle(
  edges: readonly RequestDependency[],
  predecessorId: string,
  successorId: string,
): boolean {
  if (predecessorId === successorId) return true;

  // Walk forward from the successor: reaching the predecessor means the new edge closes a loop.
  const outgoing = new Map<string, string[]>();
  for (const edge of edges) {
    if (!outgoing.has(edge.predecessorRequestId)) outgoing.set(edge.predecessorRequestId, []);
    outgoing.get(edge.predecessorRequestId)!.push(edge.successorRequestId);
  }

  const seen = new Set<string>([successorId]);
  const stack = [successorId];
  while (stack.length > 0) {
    const current = stack.pop()!;
    if (current === predecessorId) return true;
    for (const next of outgoing.get(current) ?? []) {
      if (!seen.has(next)) {
        seen.add(next);
        stack.push(next);
      }
    }
  }
  return false;
}

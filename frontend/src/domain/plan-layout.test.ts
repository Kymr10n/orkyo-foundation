import { describe, it, expect } from 'vitest';
import {
  computePlanLayout,
  splitPlanChildren,
  wouldCreateCycle,
  PLAN_NODE_WIDTH,
  PLAN_COLUMN_GAP,
} from './plan-layout';
import type { RequestPlanChild } from '@foundation/src/lib/api/request-plan-api';
import type { RequestDependency } from '@foundation/src/lib/api/request-dependency-api';

function child(id: string, sortOrder = 0): RequestPlanChild {
  return {
    id,
    name: id,
    planningMode: 'leaf',
    status: 'new',
    startTs: null,
    endTs: null,
    sortOrder,
    icon: null,
    predecessorLogic: 'all',
    predecessorLogicK: null,
    canStart: true,
    externalPredecessorCount: 0,
    externalSuccessorCount: 0,
  };
}

function edge(pred: string, succ: string): RequestDependency {
  return {
    id: `${pred}->${succ}`,
    predecessorRequestId: pred,
    successorRequestId: succ,
    predecessorName: pred,
    successorName: succ,
    dependencyType: 'finish_to_start',
    lagMinutes: 0,
    createdAt: '2026-06-01T00:00:00Z',
  };
}

const columnOf = (layout: ReturnType<typeof computePlanLayout>, id: string) =>
  layout.nodes.find((n) => n.id === id)!.column;

describe('computePlanLayout', () => {
  it('puts unconnected tasks in the first column, in sort order', () => {
    const layout = computePlanLayout([child('b', 1), child('a', 0)], []);

    expect(layout.nodes.map((n) => n.id)).toEqual(['a', 'b']);
    expect(layout.nodes.every((n) => n.column === 0)).toBe(true);
    // Different rows, or they would be drawn on top of each other.
    expect(new Set(layout.nodes.map((n) => n.row)).size).toBe(2);
  });

  it('places a successor to the right of its predecessor', () => {
    const layout = computePlanLayout([child('a'), child('b')], [edge('a', 'b')]);

    expect(columnOf(layout, 'a')).toBe(0);
    expect(columnOf(layout, 'b')).toBe(1);
  });

  it('uses the LONGEST path so no edge ever points backwards', () => {
    // a → b → c and a → c. By shortest path c would sit next to b and the a→c edge would be
    // fine, but the b→c edge would point backwards. Longest path puts c after both.
    const layout = computePlanLayout(
      [child('a'), child('b'), child('c')],
      [edge('a', 'b'), edge('b', 'c'), edge('a', 'c')],
    );

    expect(columnOf(layout, 'c')).toBe(2);
  });

  it('gives every task a column greater than all of its predecessors', () => {
    const children = ['a', 'b', 'c', 'd', 'e'].map((id, i) => child(id, i));
    const edges = [edge('a', 'c'), edge('b', 'c'), edge('c', 'd'), edge('b', 'e')];
    const layout = computePlanLayout(children, edges);

    for (const e of edges) {
      expect(columnOf(layout, e.successorRequestId))
        .toBeGreaterThan(columnOf(layout, e.predecessorRequestId));
    }
  });

  it('converts the grid to pixels', () => {
    const layout = computePlanLayout([child('a'), child('b')], [edge('a', 'b')]);

    expect(layout.nodes.find((n) => n.id === 'a')!.x).toBe(0);
    expect(layout.nodes.find((n) => n.id === 'b')!.x).toBe(PLAN_NODE_WIDTH + PLAN_COLUMN_GAP);
  });

  it('reports the extent so the canvas can size and centre itself', () => {
    const layout = computePlanLayout([child('a'), child('b')], [edge('a', 'b')]);

    expect(layout.width).toBe(2 * PLAN_NODE_WIDTH + PLAN_COLUMN_GAP);
    expect(layout.height).toBeGreaterThan(0);
  });

  it('ignores edges that leave the group', () => {
    // The planner draws only what it has both ends of; an outside edge has no node to point at.
    const layout = computePlanLayout([child('a')], [edge('outsider', 'a'), edge('a', 'downstream')]);

    expect(layout.nodes).toHaveLength(1);
    expect(columnOf(layout, 'a')).toBe(0);
    expect(layout.hasCycle).toBe(false);
  });

  it('still draws a cyclic plan, and says so', () => {
    // The server refuses to create a cycle, but a plan can be read mid-change by another
    // session. Rendering nothing would be worse than rendering a shape the user can fix.
    const layout = computePlanLayout(
      [child('a'), child('b')],
      [edge('a', 'b'), edge('b', 'a')],
    );

    expect(layout.hasCycle).toBe(true);
    expect(layout.nodes).toHaveLength(2);
  });

  it('keeps a successor level with a distant predecessor instead of packing it to row 0', () => {
    // The 416-task shape: many unlinked roots, one of them late in the column with a successor.
    // Packing every column densely from row 0 put that successor at the top and drew an edge
    // sweeping vertically across every card between them.
    const roots = Array.from({ length: 20 }, (_, i) => child(`root${i}`, i));
    const layout = computePlanLayout([...roots, child('after', 99)], [edge('root19', 'after')]);

    const rowOf = (id: string) => layout.nodes.find((n) => n.id === id)!.row;
    expect(rowOf('after')).toBe(rowOf('root19'));
  });

  it('falls back to dense packing when nothing pulls a task anywhere', () => {
    const layout = computePlanLayout([child('a', 0), child('b', 1), child('c', 2)], []);

    expect(layout.nodes.map((n) => n.row)).toEqual([0, 1, 2]);
  });

  it('never stacks two tasks on the same spot', () => {
    // Two successors of the same predecessor both want its row; the second takes the next free
    // one rather than landing on top of the first.
    const layout = computePlanLayout(
      [child('p', 0), child('x', 1), child('y', 2)],
      [edge('p', 'x'), edge('p', 'y')],
    );

    const x = layout.nodes.find((n) => n.id === 'x')!;
    const y = layout.nodes.find((n) => n.id === 'y')!;
    expect(x.row).not.toBe(y.row);
  });

  it('gives each member of a cycle its own column so the loop is drawable', () => {
    // Sharing one column gave every intra-cycle edge the same x at both ends, and the curve then
    // doubled back over its own node — which reads as a rendering fault, not as a loop.
    const layout = computePlanLayout(
      [child('a'), child('b')],
      [edge('a', 'b'), edge('b', 'a')],
    );

    expect(layout.hasCycle).toBe(true);
    const columns = layout.nodes.map((n) => n.column);
    expect(new Set(columns).size).toBe(2);
  });

  it('handles an empty plan', () => {
    const layout = computePlanLayout([], []);

    expect(layout.nodes).toEqual([]);
    expect(layout.width).toBe(0);
    expect(layout.height).toBe(0);
  });

  it('orders a column near its predecessors to keep edges from crossing', () => {
    // a(row0) → x, b(row1) → y. Ordering y before x would cross both edges.
    const layout = computePlanLayout(
      [child('a', 0), child('b', 1), child('y', 2), child('x', 3)],
      [edge('a', 'x'), edge('b', 'y')],
    );

    const rowOf = (id: string) => layout.nodes.find((n) => n.id === id)!.row;
    expect(rowOf('x')).toBeLessThan(rowOf('y'));
  });
});

describe('wouldCreateCycle', () => {
  it('refuses an edge from a task to itself', () => {
    expect(wouldCreateCycle([], 'a', 'a')).toBe(true);
  });

  it('allows an edge that only moves forward', () => {
    expect(wouldCreateCycle([edge('a', 'b')], 'b', 'c')).toBe(false);
  });

  it('catches a direct loop back', () => {
    expect(wouldCreateCycle([edge('a', 'b')], 'b', 'a')).toBe(true);
  });

  it('catches a loop through several tasks', () => {
    expect(wouldCreateCycle([edge('a', 'b'), edge('b', 'c')], 'c', 'a')).toBe(true);
  });

  it('does not mistake a diamond for a cycle', () => {
    // a → b → d and a → c: adding c → d converges, it does not loop.
    const edges = [edge('a', 'b'), edge('b', 'd'), edge('a', 'c')];
    expect(wouldCreateCycle(edges, 'c', 'd')).toBe(false);
  });

  it('terminates on a graph that already contains a cycle', () => {
    expect(wouldCreateCycle([edge('a', 'b'), edge('b', 'a')], 'a', 'c')).toBe(false);
  });
});

describe('splitPlanChildren', () => {
  const ids = (list: { id: string }[]) => list.map((c) => c.id);

  it('files a task with no dependency at all under unsequenced', () => {
    const split = splitPlanChildren([child('a', 0), child('b', 1)], []);

    expect(ids(split.sequenced)).toEqual([]);
    expect(ids(split.unsequenced)).toEqual(['a', 'b']);
  });

  it('sequences both ends of an edge and leaves the strays out', () => {
    const split = splitPlanChildren(
      [child('a', 0), child('b', 1), child('stray', 2)],
      [edge('a', 'b')],
    );

    expect(ids(split.sequenced)).toEqual(['a', 'b']);
    expect(ids(split.unsequenced)).toEqual(['stray']);
  });

  it('counts a link that leaves the group as sequenced, though no edge can be drawn', () => {
    // Filing it under "unsequenced" would be a lie: it waits for real work, just not work that
    // is on this canvas. The card already says how many links leave the group.
    const outside = { ...child('a', 0), externalPredecessorCount: 1 };
    const split = splitPlanChildren([outside, child('b', 1)], []);

    expect(ids(split.sequenced)).toEqual(['a']);
    expect(ids(split.unsequenced)).toEqual(['b']);
  });

  it('ignores an edge whose other end is not a child of this group', () => {
    const split = splitPlanChildren([child('a', 0)], [edge('a', 'somewhere-else')]);

    // The plan endpoint only returns internal edges, so this is defensive: a half-edge must not
    // promote a task the canvas would then draw with nothing attached.
    expect(ids(split.sequenced)).toEqual([]);
    expect(ids(split.unsequenced)).toEqual(['a']);
  });

  it('keeps the given order in both halves', () => {
    const split = splitPlanChildren([child('b', 1), child('a', 0)], []);

    expect(ids(split.unsequenced)).toEqual(['b', 'a']);
  });
});

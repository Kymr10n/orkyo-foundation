import { describe, it, expect } from "vitest";
import type { Request } from "@foundation/src/types/requests";
import {
  buildRequestTree,
  flattenTree,
  flattenVisibleTree,
  getAncestorIds,
  getDescendantIds,
  wouldCreateCycle,
  getDirectChildren,
  canHaveChildren,
  canBeScheduled,
  getNextSortOrder,
  computeDerivedValues,
  buildDerivedMap,
  resolveDuration,
  resolveSchedule,
  validateNode,
  validateParentChild,
} from "./request-tree";

function makeRequest(
  overrides: Partial<Request> & { id: string },
): Request {
  return {
    name: `Request ${overrides.id}`,
    minimalDurationValue: 60,
    minimalDurationUnit: "minutes",
    schedulingSettingsApply: false,
    status: "new",
    planningMode: "leaf",
    sortOrder: 0,
    createdAt: "2025-01-01T00:00:00Z",
    updatedAt: "2025-01-01T00:00:00Z",
    assignments: [],
    ...overrides,
  };
}

const flat: Request[] = [
  makeRequest({ id: "root-1", planningMode: "summary", sortOrder: 0 }),
  makeRequest({ id: "child-1", parentRequestId: "root-1", sortOrder: 0 }),
  makeRequest({ id: "child-2", parentRequestId: "root-1", sortOrder: 1 }),
  makeRequest({
    id: "grandchild-1",
    parentRequestId: "child-1",
    planningMode: "leaf",
    sortOrder: 0,
  }),
  makeRequest({ id: "root-2", planningMode: "container", sortOrder: 1 }),
  makeRequest({ id: "child-3", parentRequestId: "root-2", sortOrder: 0 }),
];

describe("buildRequestTree", () => {
  it("builds a forest with correct roots", () => {
    const tree = buildRequestTree(flat);
    expect(tree).toHaveLength(2);
    expect(tree[0].request.id).toBe("root-1");
    expect(tree[1].request.id).toBe("root-2");
  });

  it("assigns correct depths", () => {
    const tree = buildRequestTree(flat);
    expect(tree[0].depth).toBe(0);
    expect(tree[0].children[0].depth).toBe(1);
    expect(tree[0].children[0].children[0].depth).toBe(2);
  });

  it("sorts children by sortOrder", () => {
    const tree = buildRequestTree(flat);
    const root1Children = tree[0].children;
    expect(root1Children[0].request.id).toBe("child-1");
    expect(root1Children[1].request.id).toBe("child-2");
  });

  it("handles empty list", () => {
    expect(buildRequestTree([])).toEqual([]);
  });

  it("promotes orphans (parent absent from the set) to roots instead of dropping them", () => {
    // Mirrors the utilization panel: a scheduled child whose unscheduled summary parent isn't loaded.
    const orphans = [
      makeRequest({ id: "absent-parent-child-a", parentRequestId: "summary-not-loaded", sortOrder: 1 }),
      makeRequest({ id: "absent-parent-child-b", parentRequestId: "summary-not-loaded", sortOrder: 0 }),
      makeRequest({ id: "real-root", sortOrder: 2 }),
    ];
    const tree = buildRequestTree(orphans);
    const rootIds = tree.map((n) => n.request.id);

    // All three appear as roots (orphans are not lost), ordered by sortOrder.
    expect(rootIds).toEqual(["absent-parent-child-b", "absent-parent-child-a", "real-root"]);
    expect(tree.every((n) => n.depth === 0)).toBe(true);
  });
});

describe("flattenTree", () => {
  it("returns DFS pre-order", () => {
    const tree = buildRequestTree(flat);
    const entries = flattenTree(tree);
    const ids = entries.map((e) => e.request.id);
    expect(ids).toEqual([
      "root-1",
      "child-1",
      "grandchild-1",
      "child-2",
      "root-2",
      "child-3",
    ]);
  });

  it("marks hasChildren correctly", () => {
    const tree = buildRequestTree(flat);
    const entries = flattenTree(tree);
    const map = Object.fromEntries(
      entries.map((e) => [e.request.id, e.hasChildren]),
    );
    expect(map["root-1"]).toBe(true);
    expect(map["child-1"]).toBe(true);
    expect(map["grandchild-1"]).toBe(false);
    expect(map["child-2"]).toBe(false);
  });

  it("marks isLastChild correctly", () => {
    const tree = buildRequestTree(flat);
    const entries = flattenTree(tree);
    const map = Object.fromEntries(
      entries.map((e) => [e.request.id, e.isLastChild]),
    );
    expect(map["child-1"]).toBe(false);
    expect(map["child-2"]).toBe(true);
    expect(map["root-2"]).toBe(true);
  });
});

describe("getAncestorIds", () => {
  it("returns ancestors from parent to root", () => {
    expect(getAncestorIds("grandchild-1", flat)).toEqual([
      "child-1",
      "root-1",
    ]);
  });

  it("returns empty for root request", () => {
    expect(getAncestorIds("root-1", flat)).toEqual([]);
  });
});

describe("getDescendantIds", () => {
  it("returns all descendants", () => {
    const desc = getDescendantIds("root-1", flat);
    expect(desc).toHaveLength(3);
    expect(desc).toContain("child-1");
    expect(desc).toContain("child-2");
    expect(desc).toContain("grandchild-1");
  });

  it("returns empty for leaf", () => {
    expect(getDescendantIds("grandchild-1", flat)).toEqual([]);
  });
});

describe("wouldCreateCycle", () => {
  it("detects self-parenting", () => {
    expect(wouldCreateCycle("root-1", "root-1", flat)).toBe(true);
  });

  it("detects moving parent under its descendant", () => {
    expect(wouldCreateCycle("root-1", "grandchild-1", flat)).toBe(true);
  });

  it("allows valid moves", () => {
    expect(wouldCreateCycle("child-2", "root-2", flat)).toBe(false);
  });
});

describe("getDirectChildren", () => {
  it("returns sorted direct children", () => {
    const children = getDirectChildren("root-1", flat);
    expect(children.map((c) => c.id)).toEqual(["child-1", "child-2"]);
  });

  it("returns empty for leaf", () => {
    expect(getDirectChildren("grandchild-1", flat)).toEqual([]);
  });
});

describe("canHaveChildren", () => {
  it("returns true for summary", () => {
    expect(canHaveChildren("summary")).toBe(true);
  });
  it("returns true for container", () => {
    expect(canHaveChildren("container")).toBe(true);
  });
  it("returns false for leaf", () => {
    expect(canHaveChildren("leaf")).toBe(false);
  });
});

describe("canBeScheduled", () => {
  it("returns true for leaf", () => {
    expect(canBeScheduled("leaf")).toBe(true);
  });
  it("returns false for summary", () => {
    expect(canBeScheduled("summary")).toBe(false);
  });
  it("returns false for container", () => {
    expect(canBeScheduled("container")).toBe(false);
  });
});

describe("getNextSortOrder", () => {
  it("returns max + 1 for existing children", () => {
    expect(getNextSortOrder("root-1", flat)).toBe(2);
  });

  it("returns 0 for a request with no children", () => {
    expect(getNextSortOrder("grandchild-1", flat)).toBe(0);
  });
});

describe("computeDerivedValues", () => {
  const requests: Request[] = [
    makeRequest({
      id: "parent",
      planningMode: "summary",
    }),
    makeRequest({
      id: "c1",
      parentRequestId: "parent",
      startTs: "2025-06-01T08:00:00Z",
      endTs: "2025-06-01T12:00:00Z",
      minimalDurationValue: 2,
      minimalDurationUnit: "hours",
      sortOrder: 0,
    }),
    makeRequest({
      id: "c2",
      parentRequestId: "parent",
      startTs: "2025-06-02T09:00:00Z",
      endTs: "2025-06-03T17:00:00Z",
      minimalDurationValue: 3,
      minimalDurationUnit: "hours",
      sortOrder: 1,
    }),
  ];

  it("returns null for a request with no children", () => {
    expect(computeDerivedValues("c1", requests)).toBeNull();
  });

  it("computes earliest start and latest end from children", () => {
    const derived = computeDerivedValues("parent", requests)!;
    expect(derived.startTs).toBe("2025-06-01T08:00:00Z");
    expect(derived.endTs).toBe("2025-06-03T17:00:00Z");
  });

  it("computes span duration (not sum) for summary", () => {
    const derived = computeDerivedValues("parent", requests)!;
    // Span: 2025-06-01T08:00 to 2025-06-03T17:00 = 57 hours = 3420 minutes
    expect(derived.spanDurationMinutes).toBe(3420);
    // Effort sum still available
    expect(derived.totalDurationValue).toBe(5); // 2h + 3h effort
    expect(derived.totalDurationUnit).toBe("hours");
  });

  it("derives dates from all descendants, not just direct children", () => {
    const nested: Request[] = [
      makeRequest({ id: "root", planningMode: "summary" }),
      makeRequest({ id: "mid", parentRequestId: "root", planningMode: "summary", sortOrder: 0 }),
      makeRequest({
        id: "deep", parentRequestId: "mid", sortOrder: 0,
        startTs: "2025-01-01T00:00:00Z", endTs: "2025-12-31T23:59:59Z",
        minimalDurationValue: 1, minimalDurationUnit: "days",
      }),
    ];
    const derived = computeDerivedValues("root", nested)!;
    // Dates should come from grandchild
    expect(derived.startTs).toBe("2025-01-01T00:00:00Z");
    expect(derived.endTs).toBe("2025-12-31T23:59:59Z");
  });

  it("converts mixed units to the most common", () => {
    const mixed: Request[] = [
      makeRequest({ id: "p", planningMode: "container" }),
      makeRequest({
        id: "m1", parentRequestId: "p", sortOrder: 0,
        minimalDurationValue: 120, minimalDurationUnit: "minutes",
      }),
      makeRequest({
        id: "m2", parentRequestId: "p", sortOrder: 1,
        minimalDurationValue: 60, minimalDurationUnit: "minutes",
      }),
      makeRequest({
        id: "m3", parentRequestId: "p", sortOrder: 2,
        minimalDurationValue: 1, minimalDurationUnit: "hours",
      }),
    ];
    const derived = computeDerivedValues("p", mixed)!;
    // 120min + 60min + 60min = 240min, most common unit = minutes
    expect(derived.totalDurationUnit).toBe("minutes");
    expect(derived.totalDurationValue).toBe(240);
  });
});

describe("buildDerivedMap", () => {
  it("maps parent ids to derived values and omits leaves", () => {
    const map = buildDerivedMap(flat);
    expect(map.has("root-1")).toBe(true);
    expect(map.has("root-2")).toBe(true);
    expect(map.has("child-2")).toBe(false); // leaf, can't have children
    expect(map.has("grandchild-1")).toBe(false); // leaf
  });

  it("maps a childless parent to null", () => {
    const map = buildDerivedMap([
      makeRequest({ id: "empty-parent", planningMode: "summary" }),
    ]);
    expect(map.get("empty-parent")).toBeNull();
  });

  it("matches computeDerivedValues for a parent with children", () => {
    const requests: Request[] = [
      makeRequest({ id: "parent", planningMode: "summary" }),
      makeRequest({
        id: "c1",
        parentRequestId: "parent",
        startTs: "2025-06-01T08:00:00Z",
        endTs: "2025-06-01T12:00:00Z",
        sortOrder: 0,
      }),
    ];
    const map = buildDerivedMap(requests);
    expect(map.get("parent")).toEqual(computeDerivedValues("parent", requests));
  });
});

describe("resolveDuration", () => {
  const request = makeRequest({
    id: "r",
    minimalDurationValue: 45,
    minimalDurationUnit: "minutes",
  });

  it("uses the request's own minimal duration when there are no derived values", () => {
    expect(resolveDuration(request, null)).toEqual({ text: "45 minutes", isDerived: false });
  });

  it("uses derived (sum of children) values when present", () => {
    const derived = {
      startTs: null,
      endTs: null,
      spanDurationMinutes: null,
      totalDurationValue: 3,
      totalDurationUnit: "hours" as const,
      requirementCount: 0,
    };
    expect(resolveDuration(request, derived)).toEqual({ text: "3 hours", isDerived: true });
  });
});

describe("resolveSchedule", () => {
  it("uses the request's own schedule when there are no derived dates", () => {
    const request = makeRequest({
      id: "r",
      startTs: "2025-06-01T08:00:00Z",
      endTs: "2025-06-01T12:00:00Z",
    });
    const resolved = resolveSchedule(request, null);
    expect(resolved.isDerived).toBe(false);
    expect(resolved.text).toContain("—");
  });

  it("uses derived dates when both start and end are present", () => {
    const request = makeRequest({ id: "r", planningMode: "summary" });
    const derived = {
      startTs: "2025-06-01T08:00:00Z",
      endTs: "2025-06-03T17:00:00Z",
      spanDurationMinutes: 3420,
      totalDurationValue: 5,
      totalDurationUnit: "hours" as const,
      requirementCount: 0,
    };
    const resolved = resolveSchedule(request, derived);
    expect(resolved.isDerived).toBe(true);
    expect(resolved.text).not.toBeNull();
  });

  it("returns a null text when neither derived nor own dates are available", () => {
    const request = makeRequest({ id: "r" });
    expect(resolveSchedule(request, null)).toEqual({ text: null, isDerived: false });
  });
});

// ---------------------------------------------------------------------------
// flattenVisibleTree
// ---------------------------------------------------------------------------

describe("flattenVisibleTree", () => {
  it("hides children of collapsed nodes", () => {
    const tree = buildRequestTree(flat);
    const expanded = new Set(["root-1"]); // root-1 expanded, child-1 collapsed
    const entries = flattenVisibleTree(tree, expanded);
    const ids = entries.map((e) => e.request.id);
    // root-1 is expanded so child-1 and child-2 visible,
    // but child-1 is NOT expanded so grandchild-1 is hidden
    expect(ids).toContain("root-1");
    expect(ids).toContain("child-1");
    expect(ids).toContain("child-2");
    expect(ids).not.toContain("grandchild-1");
    // root-2 is collapsed so child-3 is hidden
    expect(ids).toContain("root-2");
    expect(ids).not.toContain("child-3");
  });

  it("shows all when all parents expanded", () => {
    const tree = buildRequestTree(flat);
    const expanded = new Set(["root-1", "child-1", "root-2"]);
    const entries = flattenVisibleTree(tree, expanded);
    expect(entries).toHaveLength(flat.length);
  });

  it("shows only roots when nothing expanded", () => {
    const tree = buildRequestTree(flat);
    const entries = flattenVisibleTree(tree, new Set());
    const ids = entries.map((e) => e.request.id);
    expect(ids).toEqual(["root-1", "root-2"]);
  });
});

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

describe("validateNode", () => {
  it("returns error when leaf has children", () => {
    const reqs: Request[] = [
      makeRequest({ id: "bad-leaf", planningMode: "leaf" }),
      makeRequest({ id: "child", parentRequestId: "bad-leaf", sortOrder: 0 }),
    ];
    const issues = validateNode(reqs[0], reqs);
    expect(issues).toHaveLength(1);
    expect(issues[0].code).toBe("LEAF_HAS_CHILDREN");
    expect(issues[0].severity).toBe("error");
  });

  it("returns error when start >= end", () => {
    const reqs: Request[] = [
      makeRequest({
        id: "bad-dates",
        startTs: "2025-06-02T00:00:00Z",
        endTs: "2025-06-01T00:00:00Z",
      }),
    ];
    const issues = validateNode(reqs[0], reqs);
    expect(issues.some((i) => i.code === "START_AFTER_END")).toBe(true);
  });

  it("returns no issues for valid leaf", () => {
    const reqs: Request[] = [makeRequest({ id: "ok" })];
    expect(validateNode(reqs[0], reqs)).toHaveLength(0);
  });
});

describe("validateParentChild", () => {
  it("returns error when child is outside container boundary", () => {
    const parent = makeRequest({
      id: "container",
      planningMode: "container",
      earliestStartTs: "2025-06-01T00:00:00Z",
      latestEndTs: "2025-06-30T00:00:00Z",
    });
    const child = makeRequest({
      id: "child",
      parentRequestId: "container",
      startTs: "2025-05-01T00:00:00Z", // before container
      endTs: "2025-07-01T00:00:00Z", // after container
    });
    const issues = validateParentChild(parent, child);
    expect(issues).toHaveLength(2);
    expect(issues.map((i) => i.code)).toContain("CHILD_BEFORE_CONTAINER_START");
    expect(issues.map((i) => i.code)).toContain("CHILD_AFTER_CONTAINER_END");
  });
});

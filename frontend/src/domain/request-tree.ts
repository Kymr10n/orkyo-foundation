/**
 * Pure domain logic for request tree hierarchy.
 *
 * All functions are pure — no side effects, no API calls.
 * They operate on flat request arrays and produce tree structures.
 */

import type { DurationUnit, PlanningMode, Request } from "@foundation/src/types/requests";
import {
  DURATION_TO_MINUTES,
  MS_PER_MINUTE,
  DISPLAY_PRECISION,
  ValidationCode,
} from "./constants";

// ---------------------------------------------------------------------------
// Tree node — extends a Request with computed tree relationships
// ---------------------------------------------------------------------------

interface RequestTreeNode {
  request: Request;
  children: RequestTreeNode[];
  depth: number;
}

// ---------------------------------------------------------------------------
// Build tree from flat list
// ---------------------------------------------------------------------------

/**
 * Build a forest (array of root nodes) from a flat list of requests.
 * Root requests are those with no parentRequestId.
 * Children are sorted by sortOrder, then createdAt.
 */
export function buildRequestTree(requests: Request[]): RequestTreeNode[] {
  const byId = new Map<string, Request>();
  const childrenMap = new Map<string, Request[]>();

  for (const r of requests) {
    byId.set(r.id, r);
    const parentId = r.parentRequestId ?? "__root__";
    const siblings = childrenMap.get(parentId) ?? [];
    siblings.push(r);
    childrenMap.set(parentId, siblings);
  }

  // Sort children by sortOrder, then createdAt
  for (const [, children] of childrenMap) {
    children.sort((a, b) => {
      const so = a.sortOrder - b.sortOrder;
      if (so !== 0) return so;
      return a.createdAt.localeCompare(b.createdAt);
    });
  }

  function buildNode(request: Request, depth: number): RequestTreeNode {
    const kids = childrenMap.get(request.id) ?? [];
    return {
      request,
      children: kids.map((c) => buildNode(c, depth + 1)),
      depth,
    };
  }

  const roots = childrenMap.get("__root__") ?? [];
  return roots.map((r) => buildNode(r, 0));
}

// ---------------------------------------------------------------------------
// Flatten tree to ordered list (DFS pre-order)
// ---------------------------------------------------------------------------

export interface FlatTreeEntry {
  request: Request;
  depth: number;
  hasChildren: boolean;
  isLastChild: boolean;
}

/**
 * Flatten the tree into a DFS pre-order list, useful for rendering
 * an outline/list view with indentation.
 */
export function flattenTree(roots: RequestTreeNode[]): FlatTreeEntry[] {
  const result: FlatTreeEntry[] = [];

  function walk(nodes: RequestTreeNode[]) {
    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i];
      result.push({
        request: node.request,
        depth: node.depth,
        hasChildren: node.children.length > 0,
        isLastChild: i === nodes.length - 1,
      });
      walk(node.children);
    }
  }

  walk(roots);
  return result;
}

// ---------------------------------------------------------------------------
// Tree queries
// ---------------------------------------------------------------------------

/**
 * Get all ancestor IDs for a request (from immediate parent to root).
 * Optionally accepts a pre-built byId map to avoid rebuilding it per call.
 */
export function getAncestorIds(
  requestId: string,
  requests: Request[],
  byId?: Map<string, Request>,
): string[] {
  const lookup = byId ?? new Map(requests.map((r) => [r.id, r]));
  const ancestors: string[] = [];
  let current = lookup.get(requestId);

  while (current?.parentRequestId) {
    ancestors.push(current.parentRequestId);
    current = lookup.get(current.parentRequestId);
  }

  return ancestors;
}

/**
 * Get all descendant IDs (recursive children) of a request.
 */
export function getDescendantIds(
  requestId: string,
  requests: Request[],
): string[] {
  const childrenMap = new Map<string, string[]>();
  for (const r of requests) {
    if (r.parentRequestId) {
      const siblings = childrenMap.get(r.parentRequestId) ?? [];
      siblings.push(r.id);
      childrenMap.set(r.parentRequestId, siblings);
    }
  }

  const result: string[] = [];
  const stack = [...(childrenMap.get(requestId) ?? [])];
  while (stack.length > 0) {
    const id = stack.pop()!;
    result.push(id);
    const kids = childrenMap.get(id);
    if (kids) stack.push(...kids);
  }

  return result;
}

/**
 * Check if moving `requestId` under `newParentId` would create a cycle.
 */
export function wouldCreateCycle(
  requestId: string,
  newParentId: string,
  requests: Request[],
): boolean {
  if (requestId === newParentId) return true;
  const descendants = getDescendantIds(requestId, requests);
  return descendants.includes(newParentId);
}

/**
 * Get direct children of a request from a flat list.
 */
export function getDirectChildren(
  requestId: string,
  requests: Request[],
): Request[] {
  return requests
    .filter((r) => r.parentRequestId === requestId)
    .sort((a, b) => a.sortOrder - b.sortOrder);
}

/**
 * Determine if a request can accept children based on its planning mode.
 */
export function canHaveChildren(planningMode: PlanningMode): boolean {
  return planningMode === "summary" || planningMode === "container";
}

/**
 * Determine if a request can be scheduled (placed on the calendar).
 */
export function canBeScheduled(planningMode: PlanningMode): boolean {
  return planningMode === "leaf";
}

/**
 * Compute the next sort_order for adding a child to a parent request.
 */
export function getNextSortOrder(
  parentRequestId: string,
  requests: Request[],
): number {
  const children = getDirectChildren(parentRequestId, requests);
  if (children.length === 0) return 0;
  return Math.max(...children.map((c) => c.sortOrder)) + 1;
}

// ---------------------------------------------------------------------------
// Derived values for parent (Group / Container) rows
// ---------------------------------------------------------------------------

export interface DerivedValues {
  /** Earliest startTs among all descendants */
  startTs: string | null;
  /** Latest endTs among all descendants */
  endTs: string | null;
  /** Span duration (end - start), not sum of children */
  spanDurationMinutes: number | null;
  /** Sum of children min durations (effort), converted to the most common unit */
  totalDurationValue: number;
  totalDurationUnit: DurationUnit;
  /** Total number of requirements across children (leaf-level) */
  requirementCount: number;
}

/**
 * Get all descendant requests (not just direct children) as a flat array.
 */
function getAllDescendants(requestId: string, requests: Request[]): Request[] {
  const ids = getDescendantIds(requestId, requests);
  const idSet = new Set(ids);
  return requests.filter((r) => idSet.has(r.id));
}

/**
 * Compute derived aggregate values for a parent request from ALL descendants.
 * Summary: start = min(all descendant starts), end = max(all descendant ends),
 * duration = span (end - start), NOT sum.
 * Returns null if the request has no children.
 */
export function computeDerivedValues(
  requestId: string,
  requests: Request[],
): DerivedValues | null {
  const children = getDirectChildren(requestId, requests);
  if (children.length === 0) return null;

  // Use all descendants for date roll-up (spec requirement)
  const descendants = getAllDescendants(requestId, requests);
  return computeDerivedValuesFromDescendants(children, descendants);
}

/**
 * Compute derived aggregate values from a pre-fetched list of children.
 * Use this when you already have the children array to avoid redundant lookups.
 * When descendants are provided, dates are derived from ALL descendants (spec compliant).
 */
export function computeDerivedValuesFromChildren(
  children: Request[],
  descendants?: Request[],
): DerivedValues {
  return computeDerivedValuesFromDescendants(children, descendants ?? children);
}

/**
 * Core computation: derives dates from descendants, effort from direct children.
 */
function computeDerivedValuesFromDescendants(
  children: Request[],
  descendants: Request[],
): DerivedValues {
  let earliest: string | null = null;
  let latest: string | null = null;
  let totalMinutes = 0;
  let reqCount = 0;

  // Find the most common unit among direct children for display
  const unitCounts = new Map<DurationUnit, number>();

  // Dates come from ALL descendants (per spec)
  for (const desc of descendants) {
    if (desc.startTs) {
      if (!earliest || desc.startTs < earliest) earliest = desc.startTs;
    }
    if (desc.endTs) {
      if (!latest || desc.endTs > latest) latest = desc.endTs;
    }
  }

  // Effort sum comes from direct children only
  for (const child of children) {
    const mins = child.minimalDurationValue * (DURATION_TO_MINUTES[child.minimalDurationUnit] ?? 1);
    totalMinutes += mins;
    unitCounts.set(child.minimalDurationUnit, (unitCounts.get(child.minimalDurationUnit) ?? 0) + 1);
    reqCount += child.requirements?.length ?? 0;
  }

  // Pick the most common unit
  let bestUnit: DurationUnit = "hours";
  let bestCount = 0;
  for (const [unit, count] of unitCounts) {
    if (count > bestCount) {
      bestUnit = unit;
      bestCount = count;
    }
  }

  const divisor = DURATION_TO_MINUTES[bestUnit] ?? 1;
  const totalValue = totalMinutes / divisor;

  // Span duration: calendar span from earliest start to latest end (per spec)
  let spanDurationMinutes: number | null = null;
  if (earliest && latest) {
    spanDurationMinutes = (new Date(latest).getTime() - new Date(earliest).getTime()) / MS_PER_MINUTE;
  }

  return {
    startTs: earliest,
    endTs: latest,
    spanDurationMinutes,
    totalDurationValue: Math.round(totalValue * DISPLAY_PRECISION) / DISPLAY_PRECISION,
    totalDurationUnit: bestUnit,
    requirementCount: reqCount,
  };
}

// ---------------------------------------------------------------------------
// Validation model (per spec: structured, not ad-hoc booleans)
// ---------------------------------------------------------------------------

type ValidationSeverity = "info" | "warning" | "error";

interface ValidationIssue {
  code: string;
  severity: ValidationSeverity;
  requestId: string;
  message: string;
  path?: string[];
}

/**
 * Validate a single node: required fields, planning mode consistency, date order.
 */
export function validateNode(request: Request, requests: Request[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const id = request.id;

  // Leaf must not have children
  if (request.planningMode === "leaf") {
    const children = getDirectChildren(id, requests);
    if (children.length > 0) {
      issues.push({
        code: ValidationCode.LEAF_HAS_CHILDREN,
        severity: "error",
        requestId: id,
        message: "Leaf request must not have children",
      });
    }
  }

  // start <= end
  if (request.startTs && request.endTs && request.startTs >= request.endTs) {
    issues.push({
      code: ValidationCode.START_AFTER_END,
      severity: "error",
      requestId: id,
      message: "Start must be before end",
    });
  }

  // Actual duration >= min duration
  if (
    request.actualDurationValue != null &&
    request.actualDurationUnit != null
  ) {
    const actualMin =
      request.actualDurationValue * (DURATION_TO_MINUTES[request.actualDurationUnit] ?? 1);
    const minMin =
      request.minimalDurationValue * (DURATION_TO_MINUTES[request.minimalDurationUnit] ?? 1);
    if (actualMin < minMin) {
      issues.push({
        code: ValidationCode.BELOW_MIN_DURATION,
        severity: "warning",
        requestId: id,
        message: "Actual duration is below minimum duration",
      });
    }
  }

  return issues;
}

/**
 * Validate parent-child relationships: container boundary enforcement.
 */
export function validateParentChild(
  parent: Request,
  child: Request,
): ValidationIssue[] {
  const issues: ValidationIssue[] = [];

  // Container boundary enforcement
  if (parent.planningMode === "container") {
    if (parent.earliestStartTs && child.startTs && child.startTs < parent.earliestStartTs) {
      issues.push({
        code: ValidationCode.CHILD_BEFORE_CONTAINER_START,
        severity: "error",
        requestId: child.id,
        message: `Child starts before container boundary (${parent.name})`,
        path: ["startTs"],
      });
    }
    if (parent.latestEndTs && child.endTs && child.endTs > parent.latestEndTs) {
      issues.push({
        code: ValidationCode.CHILD_AFTER_CONTAINER_END,
        severity: "error",
        requestId: child.id,
        message: `Child ends after container boundary (${parent.name})`,
        path: ["endTs"],
      });
    }
  }

  return issues;
}

// ---------------------------------------------------------------------------
// Recalculation — propagate changes upward
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Flatten visible tree (with expand/collapse filtering)
// ---------------------------------------------------------------------------

/**
 * Flatten the tree into a DFS pre-order list, filtering out children of
 * collapsed nodes. This is the primary function for rendering tree views.
 */
export function flattenVisibleTree(
  roots: RequestTreeNode[],
  expandedIds: Set<string>,
): FlatTreeEntry[] {
  const result: FlatTreeEntry[] = [];

  function walk(nodes: RequestTreeNode[]) {
    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i];
      const hasChildren = node.children.length > 0;
      result.push({
        request: node.request,
        depth: node.depth,
        hasChildren,
        isLastChild: i === nodes.length - 1,
      });
      // Only recurse into children if this node is expanded
      if (hasChildren && expandedIds.has(node.request.id)) {
        walk(node.children);
      }
    }
  }

  walk(roots);
  return result;
}



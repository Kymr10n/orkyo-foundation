import type { Request, ResourceAssignment } from '@foundation/src/types/requests';
import { DEFAULT_TARGET_RESOURCE_TYPE_KEYS } from '@foundation/src/constants';
import { randomId } from '@foundation/src/lib/core/ids';

/**
 * The resource types a request needs, with the space default applied for payloads that
 * predate the field. The single place the default is spelled out on the read path.
 */
export function getTargetResourceTypeKeys(r: Request): string[] {
  return r.targetResourceTypeKeys ?? [...DEFAULT_TARGET_RESOURCE_TYPE_KEYS];
}

/**
 * Gets the live (non-cancelled) assignment of one resource type, if any. A request holds
 * at most one resource per targeted type, so this is single-valued.
 */
export function getAssignmentOfType(r: Request, resourceTypeKey: string): ResourceAssignment | null {
  return (r.assignments ?? []).find(
    a => a.resourceTypeKey === resourceTypeKey && a.assignmentStatus !== 'Cancelled'
  ) ?? null;
}

/**
 * The resource a request occupies on the floorplan, if any.
 *
 * Takes the placeable type keys rather than assuming `space`: the backend records an assignment
 * under the resource's own type, so a request scheduled onto a tenant-defined placeable type
 * carries that key. Matching only `space` made those assignments invisible — no bar on the grid,
 * no occupancy on the plan — which is the bug that blocked placing anything but a space.
 *
 * A request holds at most one placeable resource, because one thing cannot be in two places.
 */
export function getPlacementAssignment(
  r: Request,
  placeableKeys: ReadonlySet<string>
): ResourceAssignment | null {
  return (r.assignments ?? []).find(
    a => placeableKeys.has(a.resourceTypeKey) && a.assignmentStatus !== 'Cancelled'
  ) ?? null;
}

export function getPlacementResourceId(r: Request, placeableKeys: ReadonlySet<string>): string | null {
  return getPlacementAssignment(r, placeableKeys)?.resourceId ?? null;
}

/**
 * Optimistically replaces the placement assignment on a request (client-side update).
 * Used by the schedule mutation for drag-to-grid; the server response replaces
 * this synthetic assignment when it lands.
 *
 * The caller passes the type key of the row that was dropped on, so the optimistic assignment
 * carries the same key the server will write back — otherwise the bar would jump on refetch.
 */
export function applyPlacementAssignmentOptimistic(
  r: Request,
  resourceId: string,
  resourceTypeKey: string,
  startUtc: string,
  endUtc: string,
  placeableKeys: ReadonlySet<string>
): Request {
  const otherAssignments = r.assignments.filter(a => !placeableKeys.has(a.resourceTypeKey));
  const now = new Date().toISOString();
  const newAssignment: ResourceAssignment = {
    id: `optimistic-${randomId()}`,
    resourceId,
    resourceTypeKey,
    startUtc,
    endUtc,
    assignmentStatus: 'Planned',
    createdAt: now,
    updatedAt: now,
    isOptimistic: true,
  };
  return {
    ...r,
    assignments: [...otherAssignments, newAssignment],
    startTs: startUtc,
    endTs: endUtc,
    isScheduled: true,
  };
}

/**
 * Optimistically clears the placement assignment from a request (unschedule path).
 */
export function clearPlacementAssignmentOptimistic(
  r: Request,
  placeableKeys: ReadonlySet<string>
): Request {
  return {
    ...r,
    assignments: r.assignments.filter(a => !placeableKeys.has(a.resourceTypeKey)),
    startTs: null,
    endTs: null,
    isScheduled: false,
  };
}

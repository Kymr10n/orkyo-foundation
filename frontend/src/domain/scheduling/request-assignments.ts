import type { Request, ResourceAssignment } from '@foundation/src/types/requests';
import { randomId } from '@foundation/src/lib/core/ids';

/**
 * Gets the space assignment from a request, if any.
 */
export function getSpaceAssignment(r: Request): ResourceAssignment | null {
  return (r.assignments ?? []).find(
    a => a.resourceTypeKey === 'space' && a.assignmentStatus !== 'Cancelled'
  ) ?? null;
}

/**
 * Gets the space resource ID from a request, if any.
 */
export function getSpaceResourceId(r: Request): string | null {
  return getSpaceAssignment(r)?.resourceId ?? null;
}

/**
 * Optimistically replaces the space assignment on a request (client-side update).
 * Used by the schedule mutation for drag-to-grid; the server response replaces
 * this synthetic assignment when it lands.
 */
export function applySpaceAssignmentOptimistic(
  r: Request,
  resourceId: string,
  startUtc: string,
  endUtc: string
): Request {
  const nonSpaceAssignments = r.assignments.filter(a => a.resourceTypeKey !== 'space');
  const now = new Date().toISOString();
  const newAssignment: ResourceAssignment = {
    id: `optimistic-${randomId()}`,
    resourceId,
    resourceTypeKey: 'space',
    startUtc,
    endUtc,
    assignmentStatus: 'Planned',
    createdAt: now,
    updatedAt: now,
    isOptimistic: true,
  };
  return {
    ...r,
    assignments: [...nonSpaceAssignments, newAssignment],
    startTs: startUtc,
    endTs: endUtc,
    isScheduled: true,
  };
}

/**
 * Optimistically clears the space assignment from a request (unschedule path).
 */
export function clearSpaceAssignmentOptimistic(r: Request): Request {
  return {
    ...r,
    assignments: r.assignments.filter(a => a.resourceTypeKey !== 'space'),
    startTs: null,
    endTs: null,
    isScheduled: false,
  };
}

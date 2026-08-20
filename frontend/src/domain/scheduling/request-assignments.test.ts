import { describe, it, expect } from 'vitest';
import {
  getPlacementResourceId,
  applyPlacementAssignmentOptimistic,
  clearPlacementAssignmentOptimistic,
} from './request-assignments';
import { makeRequest, makeAssignment } from '@foundation/src/test-utils/request-fixtures';

// Placement is resolved against the placeable type set now, not the literal 'space' key.
const PLACEABLE_KEYS: ReadonlySet<string> = new Set(['space']);

describe('request-assignments helpers', () => {
  describe('getPlacementResourceId', () => {
    it('returns the space resource ID when assignment exists', () => {
      const request = makeRequest({
        assignments: [makeAssignment('space-123', 'space')],
      });
      const result = getPlacementResourceId(request, PLACEABLE_KEYS);
      expect(result).toBe('space-123');
    });

    it('returns null when no space assignment exists', () => {
      const request = makeRequest({
        assignments: [makeAssignment('person-456', 'person')],
      });
      const result = getPlacementResourceId(request, PLACEABLE_KEYS);
      expect(result).toBeNull();
    });

    it('returns null when assignments is empty', () => {
      const request = makeRequest({ assignments: [] });
      const result = getPlacementResourceId(request, PLACEABLE_KEYS);
      expect(result).toBeNull();
    });

    it('returns null when assignments is undefined (should not happen but defensive)', () => {
      const request = makeRequest({ assignments: undefined as unknown as [] });
      const result = getPlacementResourceId(request, PLACEABLE_KEYS);
      expect(result).toBeNull();
    });

    it('ignores cancelled space assignments', () => {
      const request = makeRequest({
        assignments: [
          makeAssignment('space-123', 'space', { assignmentStatus: 'Cancelled' }),
          makeAssignment('space-456', 'space'),
        ],
      });
      const result = getPlacementResourceId(request, PLACEABLE_KEYS);
      expect(result).toBe('space-456');
    });
  });

  describe('applyPlacementAssignmentOptimistic', () => {
    it('sets space assignment with given resourceId and timestamps', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        assignments: [],
        startTs: null,
        endTs: null,
      });

      const result = applyPlacementAssignmentOptimistic(originalRequest, 'space-123', 'space', '2026-01-01T08:00:00Z', '2026-01-01T10:00:00Z', PLACEABLE_KEYS);

      expect(result.assignments).toHaveLength(1);
      expect(result.assignments[0]).toEqual(expect.objectContaining({
        resourceId: 'space-123',
        resourceTypeKey: 'space',
        startUtc: '2026-01-01T08:00:00Z',
        endUtc: '2026-01-01T10:00:00Z',
        assignmentStatus: 'Planned',
      }));
      expect(result.startTs).toBe('2026-01-01T08:00:00Z');
      expect(result.endTs).toBe('2026-01-01T10:00:00Z');
    });

    it('replaces existing space assignment', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        assignments: [
          makeAssignment('space-old', 'space'),
          makeAssignment('person-1', 'person'),
        ],
        startTs: '2026-01-01T08:00:00Z',
        endTs: '2026-01-01T10:00:00Z',
      });

      const result = applyPlacementAssignmentOptimistic(originalRequest, 'space-new', 'space', '2026-01-02T09:00:00Z', '2026-01-02T11:00:00Z', PLACEABLE_KEYS);

      // Should have space and person assignments
      expect(result.assignments).toHaveLength(2);
      const spaceAssignment = result.assignments.find(
        (a) => a.resourceTypeKey === 'space'
      );
      expect(spaceAssignment?.resourceId).toBe('space-new');
      expect(spaceAssignment?.startUtc).toBe('2026-01-02T09:00:00Z');
      expect(spaceAssignment?.endUtc).toBe('2026-01-02T11:00:00Z');

      const personAssignment = result.assignments.find(
        (a) => a.resourceTypeKey === 'person'
      );
      expect(personAssignment?.resourceId).toBe('person-1');

      expect(result.startTs).toBe('2026-01-02T09:00:00Z');
      expect(result.endTs).toBe('2026-01-02T11:00:00Z');
    });

    it('preserves other fields from original request', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        name: 'Original Name',
        description: 'Original Description',
        status: 'new',
        minimalDurationValue: 120,
        minimalDurationUnit: 'minutes',
      });

      const result = applyPlacementAssignmentOptimistic(originalRequest, 'space-123', 'space', '2026-01-01T08:00:00Z', '2026-01-01T10:00:00Z', PLACEABLE_KEYS);

      expect(result.name).toBe('Original Name');
      expect(result.description).toBe('Original Description');
      expect(result.status).toBe('new');
      expect(result.minimalDurationValue).toBe(120);
      expect(result.minimalDurationUnit).toBe('minutes');
    });

    it('clears assignment via clearPlacementAssignmentOptimistic (unschedule)', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        assignments: [makeAssignment('space-123', 'space')],
        startTs: '2026-01-01T08:00:00Z',
        endTs: '2026-01-01T10:00:00Z',
      });

      const result = clearPlacementAssignmentOptimistic(originalRequest, PLACEABLE_KEYS);

      expect(result.assignments).toHaveLength(0);
      expect(result.startTs).toBeNull();
      expect(result.endTs).toBeNull();
    });
  });
});

describe('placement resolution across placeable types', () => {
  // The bug this migration exists to fix. The backend records an assignment under the resource's
  // own type, so a request scheduled onto a tenant-defined placeable type carries that key.
  // Matching only 'space' made those assignments invisible: no bar on the grid, no occupancy on
  // the floorplan, and resize/move silently refusing to start.
  const BOOTH_AND_SPACE: ReadonlySet<string> = new Set(['space', 'booth']);

  it('finds a placement recorded under a tenant-defined placeable type', () => {
    const request = makeRequest({ assignments: [makeAssignment('booth-7', 'booth')] });

    expect(getPlacementResourceId(request, BOOTH_AND_SPACE)).toBe('booth-7');
    // The old space-only resolution is what this replaces.
    expect(getPlacementResourceId(request, PLACEABLE_KEYS)).toBeNull();
  });

  it('ignores assignments whose type cannot be placed', () => {
    const request = makeRequest({
      assignments: [makeAssignment('person-1', 'person'), makeAssignment('booth-7', 'booth')],
    });

    expect(getPlacementResourceId(request, BOOTH_AND_SPACE)).toBe('booth-7');
  });

  it('moves a request between placeable types without leaving the old placement behind', () => {
    // A drag from a space row to a booth row must replace the placement, not add a second one —
    // filtering by the space key alone would have left both.
    const request = makeRequest({
      assignments: [makeAssignment('space-1', 'space'), makeAssignment('person-1', 'person')],
    });

    const moved = applyPlacementAssignmentOptimistic(
      request,
      'booth-7',
      'booth',
      '2026-01-01T08:00:00Z',
      '2026-01-01T10:00:00Z',
      BOOTH_AND_SPACE,
    );

    expect(moved.assignments.filter((a) => BOOTH_AND_SPACE.has(a.resourceTypeKey))).toHaveLength(1);
    expect(getPlacementResourceId(moved, BOOTH_AND_SPACE)).toBe('booth-7');
    // Non-placeable assignments are untouched — a request can need a person and a booth.
    expect(moved.assignments.some((a) => a.resourceTypeKey === 'person')).toBe(true);
  });

  it('clears a placement of any placeable type on unschedule', () => {
    const request = makeRequest({
      assignments: [makeAssignment('booth-7', 'booth'), makeAssignment('person-1', 'person')],
    });

    const cleared = clearPlacementAssignmentOptimistic(request, BOOTH_AND_SPACE);

    expect(getPlacementResourceId(cleared, BOOTH_AND_SPACE)).toBeNull();
    expect(cleared.isScheduled).toBe(false);
    expect(cleared.assignments.some((a) => a.resourceTypeKey === 'person')).toBe(true);
  });
});

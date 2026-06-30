import { describe, it, expect } from 'vitest';
import {
  getSpaceAssignment,
  getSpaceResourceId,
  applySpaceAssignmentOptimistic,
  clearSpaceAssignmentOptimistic,
} from './request-assignments';
import { makeRequest, makeAssignment } from '@foundation/src/test-utils/request-fixtures';

describe('request-assignments helpers', () => {
  describe('getSpaceAssignment', () => {
    it('returns the space assignment when present', () => {
      const spaceAssignment = makeAssignment('space-123', 'space');
      const request = makeRequest({
        assignments: [
          spaceAssignment,
          makeAssignment('person-456', 'person'),
        ],
      });
      const result = getSpaceAssignment(request);
      expect(result).toBe(spaceAssignment);
    });

    it('returns null when no space assignment exists', () => {
      const request = makeRequest({
        assignments: [
          makeAssignment('person-456', 'person'),
          makeAssignment('tool-789', 'tool'),
        ],
      });
      const result = getSpaceAssignment(request);
      expect(result).toBeNull();
    });

    it('returns null when assignments is empty', () => {
      const request = makeRequest({ assignments: [] });
      const result = getSpaceAssignment(request);
      expect(result).toBeNull();
    });

    it('returns null when assignments is undefined (should not happen but defensive)', () => {
      const request = makeRequest({ assignments: undefined as unknown as [] });
      const result = getSpaceAssignment(request);
      expect(result).toBeNull();
    });

    it('ignores cancelled space assignments', () => {
      const cancelledAssignment = makeAssignment('space-123', 'space', {
        assignmentStatus: 'Cancelled',
      });
      const activeAssignment = makeAssignment('space-456', 'space');
      const request = makeRequest({
        assignments: [cancelledAssignment, activeAssignment],
      });
      const result = getSpaceAssignment(request);
      expect(result).toBe(activeAssignment);
    });
  });

  describe('getSpaceResourceId', () => {
    it('returns the space resource ID when assignment exists', () => {
      const request = makeRequest({
        assignments: [makeAssignment('space-123', 'space')],
      });
      const result = getSpaceResourceId(request);
      expect(result).toBe('space-123');
    });

    it('returns null when no space assignment exists', () => {
      const request = makeRequest({
        assignments: [makeAssignment('person-456', 'person')],
      });
      const result = getSpaceResourceId(request);
      expect(result).toBeNull();
    });

    it('returns null when assignments is empty', () => {
      const request = makeRequest({ assignments: [] });
      const result = getSpaceResourceId(request);
      expect(result).toBeNull();
    });

    it('ignores cancelled space assignments', () => {
      const request = makeRequest({
        assignments: [
          makeAssignment('space-123', 'space', { assignmentStatus: 'Cancelled' }),
          makeAssignment('space-456', 'space'),
        ],
      });
      const result = getSpaceResourceId(request);
      expect(result).toBe('space-456');
    });
  });

  describe('applySpaceAssignmentOptimistic', () => {
    it('sets space assignment with given resourceId and timestamps', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        assignments: [],
        startTs: null,
        endTs: null,
      });

      const result = applySpaceAssignmentOptimistic(
        originalRequest,
        'space-123',
        '2026-01-01T08:00:00Z',
        '2026-01-01T10:00:00Z'
      );

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

      const result = applySpaceAssignmentOptimistic(
        originalRequest,
        'space-new',
        '2026-01-02T09:00:00Z',
        '2026-01-02T11:00:00Z'
      );

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

      const result = applySpaceAssignmentOptimistic(
        originalRequest,
        'space-123',
        '2026-01-01T08:00:00Z',
        '2026-01-01T10:00:00Z'
      );

      expect(result.name).toBe('Original Name');
      expect(result.description).toBe('Original Description');
      expect(result.status).toBe('new');
      expect(result.minimalDurationValue).toBe(120);
      expect(result.minimalDurationUnit).toBe('minutes');
    });

    it('clears assignment via clearSpaceAssignmentOptimistic (unschedule)', () => {
      const originalRequest = makeRequest({
        id: 'req-1',
        assignments: [makeAssignment('space-123', 'space')],
        startTs: '2026-01-01T08:00:00Z',
        endTs: '2026-01-01T10:00:00Z',
      });

      const result = clearSpaceAssignmentOptimistic(originalRequest);

      expect(result.assignments).toHaveLength(0);
      expect(result.startTs).toBeNull();
      expect(result.endTs).toBeNull();
    });
  });
});

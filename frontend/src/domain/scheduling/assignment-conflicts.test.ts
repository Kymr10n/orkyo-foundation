import { describe, it, expect } from 'vitest';
import { capabilityConflictsFromValidation } from './assignment-conflicts';
import type { AssignmentValidationBatchItem } from '@foundation/src/lib/api/resource-assignments-api';

function item(overrides: Partial<AssignmentValidationBatchItem> = {}): AssignmentValidationBatchItem {
  return {
    requestId: 'r1',
    resourceId: 'space-1',
    result: { severity: 'ok', blockers: [], warnings: [] },
    ...overrides,
  };
}

describe('capabilityConflictsFromValidation', () => {
  it('maps a capability.missing blocker to a connector_mismatch error conflict', () => {
    const conflicts = capabilityConflictsFromValidation(item({
      result: {
        severity: 'blocker',
        blockers: [{ code: 'capability.missing', message: 'x', criterionId: 'c1', details: 'Crane' }],
        warnings: [],
      },
    }));
    expect(conflicts).toHaveLength(1);
    expect(conflicts[0]).toMatchObject({
      id: 'r1-c1-capability',
      kind: 'connector_mismatch',
      severity: 'error',
    });
    expect(conflicts[0].message).toContain('Crane');
  });

  it('maps resource.type-mismatch to a capability conflict', () => {
    const conflicts = capabilityConflictsFromValidation(item({
      result: {
        severity: 'blocker',
        blockers: [{ code: 'resource.type-mismatch', message: 'x' }],
        warnings: [],
      },
    }));
    expect(conflicts).toHaveLength(1);
    expect(conflicts[0].kind).toBe('connector_mismatch');
  });

  it('ignores scheduling/overbook/off-time codes (handled client-side)', () => {
    const conflicts = capabilityConflictsFromValidation(item({
      result: {
        severity: 'blocker',
        blockers: [
          { code: 'assignment.overbooked', message: 'x' },
          { code: 'offtime.overlap', message: 'x' },
          { code: 'nonworking.weekend', message: 'x' },
        ],
        warnings: [],
      },
    }));
    expect(conflicts).toEqual([]);
  });

  it('returns [] when the entry has no requestId', () => {
    const conflicts = capabilityConflictsFromValidation(item({
      requestId: undefined,
      result: {
        severity: 'blocker',
        blockers: [{ code: 'capability.missing', message: 'x' }],
        warnings: [],
      },
    }));
    expect(conflicts).toEqual([]);
  });

  it('returns [] for a clean (ok) result', () => {
    expect(capabilityConflictsFromValidation(item())).toEqual([]);
  });
});

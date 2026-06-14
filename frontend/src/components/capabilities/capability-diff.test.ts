import { describe, it, expect } from 'vitest';
import { diffCapabilityAssignments } from './capability-diff';
import type { CriterionValue } from '@foundation/src/types/criterion';

const existing = [
  { criterionId: 'c1', id: 'cap-1' },
  { criterionId: 'c2', id: 'cap-2' },
];

function desired(entries: [string, CriterionValue | null][]) {
  return new Map<string, CriterionValue | null>(entries);
}

describe('diffCapabilityAssignments', () => {
  it('upsert mode persists every non-null desired value', () => {
    const { toPersist, toDeleteIds } = diffCapabilityAssignments(
      existing,
      desired([['c1', true], ['c2', 5], ['c3', 'x']]),
      'upsert',
    );
    expect(toPersist).toEqual([
      { criterionId: 'c1', value: true },
      { criterionId: 'c2', value: 5 },
      { criterionId: 'c3', value: 'x' },
    ]);
    expect(toDeleteIds).toEqual([]);
  });

  it('add-new mode persists only criteria not already present', () => {
    const { toPersist, toDeleteIds } = diffCapabilityAssignments(
      existing,
      desired([['c1', true], ['c2', 5], ['c3', 'x']]),
      'add-new',
    );
    // c1 & c2 already exist → skipped in add-new; c3 is new → persisted. Nothing removed.
    expect(toPersist).toEqual([{ criterionId: 'c3', value: 'x' }]);
    expect(toDeleteIds).toEqual([]);
  });

  it('deletes existing capabilities whose criterion is no longer desired', () => {
    const { toPersist, toDeleteIds } = diffCapabilityAssignments(
      existing,
      desired([['c1', true]]),
      'upsert',
    );
    expect(toPersist).toEqual([{ criterionId: 'c1', value: true }]);
    expect(toDeleteIds).toEqual(['cap-2']);
  });

  it('skips null desired values (no value chosen yet)', () => {
    const { toPersist } = diffCapabilityAssignments(
      existing,
      desired([['c1', null], ['c3', null]]),
      'upsert',
    );
    expect(toPersist).toEqual([]);
  });

  it('handles an empty desired map by deleting all existing', () => {
    const { toPersist, toDeleteIds } = diffCapabilityAssignments(existing, desired([]), 'add-new');
    expect(toPersist).toEqual([]);
    expect(toDeleteIds).toEqual(['cap-1', 'cap-2']);
  });
});

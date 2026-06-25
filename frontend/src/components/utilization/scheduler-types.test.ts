import { describe, it, expect } from 'vitest';
import { groupRowsByResourceGroup } from './scheduler-types';
import type { ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';

interface Row {
  id: string;
  groups?: string[];
}

const group = (over: Partial<ResourceGroupInfo> & { id: string }): ResourceGroupInfo => ({
  name: over.id,
  defaultAvailabilityPercent: 100,
  memberCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  resourceTypeKey: 'person',
  ...over,
});

const getGroupIds = (r: Row) => r.groups ?? [];

describe('groupRowsByResourceGroup', () => {
  it('orders groups by displayOrder and appends Ungrouped last', () => {
    const groups = [
      group({ id: 'b', displayOrder: 2 }),
      group({ id: 'a', displayOrder: 1 }),
    ];
    const rows: Row[] = [
      { id: 'r-b', groups: ['b'] },
      { id: 'r-a', groups: ['a'] },
      { id: 'r-none' },
    ];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: false,
    });

    expect(result.map((g) => g.id)).toEqual(['a', 'b', 'ungrouped']);
    expect(result[2].name).toBe('Ungrouped');
    expect(result[0].rows.map((r) => r.id)).toEqual(['r-a']);
  });

  it('preserves caller row order within each bucket', () => {
    const groups = [group({ id: 'a', displayOrder: 1 })];
    const rows: Row[] = [
      { id: 'r2', groups: ['a'] },
      { id: 'r1', groups: ['a'] },
    ];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: false,
    });

    expect(result[0].rows.map((r) => r.id)).toEqual(['r2', 'r1']);
  });

  it('drops empty named groups when includeEmpty is false (Spaces grid)', () => {
    const groups = [
      group({ id: 'a', displayOrder: 1 }),
      group({ id: 'empty', displayOrder: 2 }),
    ];
    const rows: Row[] = [{ id: 'r-a', groups: ['a'] }];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: false,
    });

    expect(result.map((g) => g.id)).toEqual(['a']);
  });

  it('keeps empty named groups when includeEmpty is true (People grid)', () => {
    const groups = [
      group({ id: 'a', displayOrder: 1 }),
      group({ id: 'empty', displayOrder: 2 }),
    ];
    const rows: Row[] = [{ id: 'r-a', groups: ['a'] }];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: true,
    });

    expect(result.map((g) => g.id)).toEqual(['a', 'empty']);
    expect(result[1].rows).toEqual([]);
  });

  it('omits the Ungrouped bucket when no rows are ungrouped, regardless of includeEmpty', () => {
    const groups = [group({ id: 'a', displayOrder: 1 })];
    const rows: Row[] = [{ id: 'r-a', groups: ['a'] }];

    for (const includeEmpty of [true, false]) {
      const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
        includeEmpty,
      });
      expect(result.some((g) => g.id === 'ungrouped')).toBe(false);
    }
  });

  it('places a multi-group row in the first matching group by displayOrder (first-wins dedup)', () => {
    const groups = [
      group({ id: 'second', displayOrder: 2 }),
      group({ id: 'first', displayOrder: 1 }),
    ];
    // Row lists the ids in the opposite order to displayOrder; the helper must
    // still pick the displayOrder-first group and show the row only once.
    const rows: Row[] = [{ id: 'r', groups: ['second', 'first'] }];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: true,
    });

    const firstBucket = result.find((g) => g.id === 'first');
    const secondBucket = result.find((g) => g.id === 'second');
    expect(firstBucket?.rows.map((r) => r.id)).toEqual(['r']);
    expect(secondBucket?.rows).toEqual([]);
  });

  it('treats a row whose group ids are all unknown as ungrouped', () => {
    const groups = [group({ id: 'a', displayOrder: 1 })];
    const rows: Row[] = [{ id: 'r-stale', groups: ['deleted-group'] }];

    const result = groupRowsByResourceGroup(rows, groups, getGroupIds, {
      includeEmpty: false,
    });

    expect(result.map((g) => g.id)).toEqual(['ungrouped']);
    expect(result[0].rows.map((r) => r.id)).toEqual(['r-stale']);
  });
});

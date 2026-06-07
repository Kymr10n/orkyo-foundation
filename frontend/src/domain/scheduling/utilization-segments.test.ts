import { describe, it, expect } from 'vitest';
import {
  deriveBucketStatus,
  mergeBucketsToSegments,
  segmentDisplayData,
  type PersonUtilizationSegment,
} from './utilization-segments';
import type { ResourceUtilizationBucket } from '@foundation/src/lib/api/resource-utilization-api';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';

const RID = 'p-1';

/** One-hour bucket factory; start auto-derives end = start + 1h unless given. */
function bucket(
  start: string,
  end: string,
  overrides: Partial<ResourceUtilizationBucket> = {},
): ResourceUtilizationBucket {
  return {
    start,
    end,
    allocatedPercent: 0,
    effectiveAvailabilityPercent: 100,
    isExclusiveOccupied: false,
    ...overrides,
  };
}

// Contiguous hour boundaries used across the merge tests.
const T = {
  h8: '2026-01-06T08:00:00Z',
  h9: '2026-01-06T09:00:00Z',
  h10: '2026-01-06T10:00:00Z',
  h11: '2026-01-06T11:00:00Z',
};

describe('deriveBucketStatus', () => {
  it('non-working when effective availability is 0', () => {
    expect(
      deriveBucketStatus(bucket(T.h8, T.h9, { effectiveAvailabilityPercent: 0 }), RID, []),
    ).toBe('non-working');
  });

  it('non-working when the bucket overlaps an off-time range', () => {
    const off: OffTimeRange[] = [
      {
        id: 'off-1',
        title: 'Holiday',
        startMs: new Date(T.h8).getTime(),
        endMs: new Date(T.h9).getTime(),
        resourceIds: null,
      },
    ];
    expect(deriveBucketStatus(bucket(T.h8, T.h9), RID, off)).toBe('non-working');
  });

  it('assigned when exclusively occupied', () => {
    expect(
      deriveBucketStatus(bucket(T.h8, T.h9, { isExclusiveOccupied: true }), RID, []),
    ).toBe('assigned');
  });

  it('available when nothing allocated', () => {
    expect(deriveBucketStatus(bucket(T.h8, T.h9, { allocatedPercent: 0 }), RID, [])).toBe(
      'available',
    );
  });

  it('partial when allocated below availability', () => {
    expect(deriveBucketStatus(bucket(T.h8, T.h9, { allocatedPercent: 50 }), RID, [])).toBe(
      'partial',
    );
  });

  it('overbooked only when allocated strictly exceeds availability', () => {
    expect(
      deriveBucketStatus(
        bucket(T.h8, T.h9, { allocatedPercent: 120, effectiveAvailabilityPercent: 100 }),
        RID,
        [],
      ),
    ).toBe('overbooked');
  });

  it('partial (fully booked), not overbooked, when allocated exactly equals availability', () => {
    expect(
      deriveBucketStatus(
        bucket(T.h8, T.h9, { allocatedPercent: 100, effectiveAvailabilityPercent: 100 }),
        RID,
        [],
      ),
    ).toBe('partial');
  });
});

describe('mergeBucketsToSegments', () => {
  it('returns [] for an empty series', () => {
    expect(mergeBucketsToSegments([], RID, [])).toEqual([]);
  });

  it('merges adjacent same-status buckets into one segment', () => {
    const segs = mergeBucketsToSegments(
      [bucket(T.h8, T.h9), bucket(T.h9, T.h10)],
      RID,
      [],
    );
    expect(segs).toHaveLength(1);
    expect(segs[0]).toMatchObject({
      start: T.h8,
      end: T.h10,
      status: 'available',
      sourceUnitCount: 2,
    });
  });

  it('does NOT merge same-status buckets separated by a time gap', () => {
    // h8–h9 then h10–h11: same status but non-contiguous (h9 != h10 boundary).
    const segs = mergeBucketsToSegments(
      [bucket(T.h8, T.h9), bucket(T.h10, T.h11)],
      RID,
      [],
    );
    expect(segs).toHaveLength(2);
  });

  it('does NOT merge partial with assigned', () => {
    const segs = mergeBucketsToSegments(
      [
        bucket(T.h8, T.h9, { allocatedPercent: 50 }),
        bucket(T.h9, T.h10, { isExclusiveOccupied: true }),
      ],
      RID,
      [],
    );
    expect(segs.map((s) => s.status)).toEqual(['partial', 'assigned']);
  });

  it('does NOT merge non-working with available', () => {
    const segs = mergeBucketsToSegments(
      [
        bucket(T.h8, T.h9, { effectiveAvailabilityPercent: 0 }),
        bucket(T.h9, T.h10),
      ],
      RID,
      [],
    );
    expect(segs.map((s) => s.status)).toEqual(['non-working', 'available']);
  });

  it('keeps a single overbooked bucket as its own segment between available runs', () => {
    const segs = mergeBucketsToSegments(
      [
        bucket(T.h8, T.h9),
        bucket(T.h9, T.h10, { allocatedPercent: 120 }),
        bucket(T.h10, T.h11),
      ],
      RID,
      [],
    );
    expect(segs.map((s) => s.status)).toEqual(['available', 'overbooked', 'available']);
    expect(segs[1]).toMatchObject({ start: T.h9, end: T.h10, sourceUnitCount: 1 });
  });

  it('collapses an all-same contiguous series into one full-span segment', () => {
    const segs = mergeBucketsToSegments(
      [bucket(T.h8, T.h9), bucket(T.h9, T.h10), bucket(T.h10, T.h11)],
      RID,
      [],
    );
    expect(segs).toHaveLength(1);
    expect(segs[0]).toMatchObject({ start: T.h8, end: T.h11, sourceUnitCount: 3 });
  });

  it('reports utilizationPercent as the rounded average across merged buckets', () => {
    const segs = mergeBucketsToSegments(
      [
        bucket(T.h8, T.h9, { allocatedPercent: 50 }),
        bucket(T.h9, T.h10, { allocatedPercent: 70 }),
      ],
      RID,
      [],
    );
    expect(segs).toHaveLength(1);
    expect(segs[0].utilizationPercent).toBe(60);
  });
});

describe('segmentDisplayData', () => {
  const viewStart = new Date(T.h8).getTime();
  const viewEnd = new Date(T.h11).getTime(); // 3-hour window

  function seg(start: string, end: string): PersonUtilizationSegment {
    return { start, end, status: 'available', utilizationPercent: 0, sourceUnitCount: 1 };
  }

  it('spans the full window → left 0, width 100', () => {
    expect(segmentDisplayData(seg(T.h8, T.h11), viewStart, viewEnd)).toEqual({
      leftPercent: 0,
      widthPercent: 100,
    });
  });

  it('positions a left-aligned third of the window', () => {
    const { leftPercent, widthPercent } = segmentDisplayData(seg(T.h8, T.h9), viewStart, viewEnd);
    expect(leftPercent).toBe(0);
    expect(widthPercent).toBeCloseTo(33.333, 2);
  });

  it('clips a segment that runs past the right edge', () => {
    // h10 → h11+? extend end one hour beyond the window; width clamps to the last hour.
    const { leftPercent, widthPercent } = segmentDisplayData(
      seg(T.h10, '2026-01-06T12:00:00Z'),
      viewStart,
      viewEnd,
    );
    expect(leftPercent).toBeCloseTo(66.667, 2);
    expect(widthPercent).toBeCloseTo(33.333, 2);
  });
});

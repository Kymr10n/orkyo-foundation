import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportRequests, importRequests } from './export-handlers';
import type { Request } from '@foundation/src/types/requests';

const downloaded: { content: string; filename: string }[] = [];
vi.mock('./import-export', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    downloadFile: (content: string, filename: string) => downloaded.push({ content, filename }),
  };
});

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'req-1',
    name: 'Bracket run',
    description: 'First batch',
    status: 'new',
    startTs: '2026-08-10T00:00:00Z',
    endTs: '2026-08-15T00:00:00Z',
    earliestStartTs: '2026-08-08T00:00:00Z',
    latestEndTs: '2026-08-20T00:00:00Z',
    minimalDurationValue: 3,
    minimalDurationUnit: 'days',
    actualDurationValue: 4,
    actualDurationUnit: 'days',
    assignments: [
      { resourceId: 'space-9', resourceTypeKey: 'space', assignmentStatus: 'confirmed' },
    ],
    requirements: [],
    ...overrides,
  } as unknown as Request;
}

function fileOf(content: string) {
  return { text: () => Promise.resolve(content) } as unknown as File;
}

beforeEach(() => {
  downloaded.length = 0;
});

describe('requests export → import round-trip', () => {
  it('re-imports its own export as a valid camelCase create payload', async () => {
    await exportRequests([makeRequest()], 'csv');
    const payloads = await importRequests(fileOf(downloaded[0].content), 'csv');

    expect(payloads).toHaveLength(1);
    const p = payloads[0];
    // The regression this file exists for: the importer used to emit snake_case
    // keys (start_ts, min_duration_value, resource_id) that the create endpoint
    // silently dropped, losing dates, durations, and the space assignment.
    expect(p.name).toBe('Bracket run');
    expect(p.startTs).toBe('2026-08-10T00:00:00Z');
    expect(p.endTs).toBe('2026-08-15T00:00:00Z');
    expect(p.earliestStartTs).toBe('2026-08-08T00:00:00Z');
    expect(p.latestEndTs).toBe('2026-08-20T00:00:00Z');
    expect(p.minimalDurationValue).toBe(3);
    expect(p.minimalDurationUnit).toBe('days');
    expect(p.actualDurationValue).toBe(4);
    expect(p.actualDurationUnit).toBe('days');
    expect(p.status).toBe('new');
    expect(p.resourceIds).toEqual(['space-9']);
    // An import creates, it does not overwrite.
    expect('id' in p && (p as Record<string, unknown>).id).toBeFalsy();
    expect('start_ts' in p).toBe(false);
  });

  it('defaults the required minimal duration when the columns are empty or invalid', async () => {
    const csv = 'name,min_duration_value,min_duration_unit\nRush job,,\nOdd row,abc,fortnights';
    const payloads = await importRequests(fileOf(csv), 'csv');

    expect(payloads).toHaveLength(2);
    for (const p of payloads) {
      expect(p.minimalDurationValue).toBe(1);
      expect(p.minimalDurationUnit).toBe('hours');
    }
  });

  it('drops rows without a name and omits unknown statuses', async () => {
    const csv = 'name,status\n,new\nReal job,someday';
    const payloads = await importRequests(fileOf(csv), 'csv');

    expect(payloads).toHaveLength(1);
    expect(payloads[0].name).toBe('Real job');
    expect(payloads[0].status).toBeUndefined();
  });

  it('omits resourceIds when the export had no space assignment', async () => {
    await exportRequests([makeRequest({ assignments: [] } as Partial<Request>)], 'csv');
    const payloads = await importRequests(fileOf(downloaded[0].content), 'csv');

    expect(payloads[0].resourceIds).toBeUndefined();
  });
});

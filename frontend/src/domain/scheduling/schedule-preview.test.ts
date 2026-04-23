import { describe, it, expect } from 'vitest';
import { buildPreviewSchedule } from './schedule-preview';
import type { DraftResize } from './schedule-model';
import type { Request } from '@foundation/src/types/requests';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const T = (iso: string) => new Date(iso).getTime();

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'req-1',
    name: 'Test',
    status: 'planned',
    planningMode: 'leaf',
    sortOrder: 0,
    minimalDurationValue: 1,
    minimalDurationUnit: 'hours',
    schedulingSettingsApply: true,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

function makeScheduledRequest(id = 'req-1', spaceId = 's1'): Request {
  return makeRequest({
    id,
    spaceId,
    startTs: '2024-06-01T08:00:00.000Z',
    endTs: '2024-06-01T10:00:00.000Z',
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('buildPreviewSchedule', () => {
  it('returns an empty map for no requests', () => {
    const preview = buildPreviewSchedule([], null);
    expect(preview.size).toBe(0);
  });

  it('skips unscheduled requests', () => {
    const req = makeRequest(); // no spaceId / timestamps
    const preview = buildPreviewSchedule([req], null);
    expect(preview.size).toBe(0);
  });

  it('includes a scheduled request with committed bounds when no draft', () => {
    const req = makeScheduledRequest();
    const preview = buildPreviewSchedule([req], null);
    expect(preview.size).toBe(1);
    const entry = preview.get('req-1')!;
    expect(entry.isDraft).toBe(false);
    expect(entry.startMs).toBe(T('2024-06-01T08:00:00.000Z'));
    expect(entry.endMs).toBe(T('2024-06-01T10:00:00.000Z'));
    expect(entry.spaceId).toBe('s1');
  });

  it('applies draft bounds for the request being resized', () => {
    const req = makeScheduledRequest();
    const draft: DraftResize = {
      kind: 'resize',
      phase: 'active',
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: T('2024-06-01T08:00:00.000Z'),
      committedEndMs: T('2024-06-01T10:00:00.000Z'),
      previewStartMs: T('2024-06-01T08:00:00.000Z'),
      previewEndMs: T('2024-06-01T12:00:00.000Z'), // extended by 2 h
    };
    const preview = buildPreviewSchedule([req], draft);
    const entry = preview.get('req-1')!;
    expect(entry.isDraft).toBe(true);
    expect(entry.endMs).toBe(T('2024-06-01T12:00:00.000Z'));
    expect(entry.startMs).toBe(T('2024-06-01T08:00:00.000Z'));
  });

  it('uses committed bounds for requests NOT being resized', () => {
    const r1 = makeScheduledRequest('req-1', 's1');
    const r2 = makeScheduledRequest('req-2', 's2');
    const draft: DraftResize = {
      kind: 'resize',
      phase: 'active',
      requestId: 'req-1',
      spaceId: 's1',
      edge: 'right',
      committedStartMs: T('2024-06-01T08:00:00.000Z'),
      committedEndMs: T('2024-06-01T10:00:00.000Z'),
      previewStartMs: T('2024-06-01T08:00:00.000Z'),
      previewEndMs: T('2024-06-01T12:00:00.000Z'),
    };
    const preview = buildPreviewSchedule([r1, r2], draft);
    const e2 = preview.get('req-2')!;
    expect(e2.isDraft).toBe(false);
    expect(e2.endMs).toBe(T('2024-06-01T10:00:00.000Z'));
  });

  it('handles multiple scheduled requests with no draft', () => {
    const requests = [
      makeScheduledRequest('req-1', 's1'),
      makeScheduledRequest('req-2', 's1'),
      makeScheduledRequest('req-3', 's2'),
    ];
    const preview = buildPreviewSchedule(requests, null);
    expect(preview.size).toBe(3);
    for (const entry of preview.values()) {
      expect(entry.isDraft).toBe(false);
    }
  });

  it('preserves minimalDurationMs from the request', () => {
    const req = makeRequest({
      id: 'req-1',
      spaceId: 's1',
      startTs: '2024-06-01T08:00:00.000Z',
      endTs: '2024-06-01T10:00:00.000Z',
      minimalDurationValue: 2,
      minimalDurationUnit: 'hours',
    });
    const preview = buildPreviewSchedule([req], null);
    expect(preview.get('req-1')!.minimalDurationMs).toBe(7_200_000);
  });
});

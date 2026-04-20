import { describe, it, expect } from 'vitest';
import { validateSpaceRequirements } from './capability-matcher';
import type { Request } from '@/types/requests';
import type { SpaceCapability } from '@/lib/api/space-capability-api';

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'req-1',
    name: 'Test Request',
    description: '',
    status: 'pending',
    spaceId: null,
    startTs: null,
    endTs: null,
    minimalDurationValue: 1,
    minimalDurationUnit: 'hours',
    requirements: [],
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  } as Request;
}

function makeCap(criterionId: string, value: unknown, dataType: string, name = 'Cap'): SpaceCapability {
  return {
    id: `cap-${criterionId}`,
    spaceId: 'space-1',
    criterionId,
    value,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    criterion: { id: criterionId, name, dataType },
  } as SpaceCapability;
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe('validateSpaceRequirements', () => {
  it('returns empty array when request has no requirements', () => {
    const result = validateSpaceRequirements(makeRequest(), []);
    expect(result).toEqual([]);
  });

  it('returns empty array when requirements is undefined', () => {
    const result = validateSpaceRequirements(
      makeRequest({ requirements: undefined as never }),
      [],
    );
    expect(result).toEqual([]);
  });

  it('returns conflict when space is missing a required capability', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 10, criterion: { id: 'c1', name: 'Seats', dataType: 'Number' } },
      ],
    });
    const result = validateSpaceRequirements(request, []);
    expect(result).toHaveLength(1);
    expect(result[0].kind).toBe('connector_mismatch');
    expect(result[0].severity).toBe('error');
  });

  // ── Number type ──

  it('returns no conflict when numeric capability meets requirement', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 10, criterion: { id: 'c1', name: 'Seats', dataType: 'Number' } },
      ],
    });
    const caps = [makeCap('c1', 15, 'Number', 'Seats')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  it('returns conflict when numeric capability is insufficient', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 20, criterion: { id: 'c1', name: 'Seats', dataType: 'Number' } },
      ],
    });
    const caps = [makeCap('c1', 10, 'Number', 'Seats')];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(1);
    expect(result[0].kind).toBe('load_exceeded');
  });

  it('returns no conflict when numeric capability exactly meets requirement', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 10, criterion: { id: 'c1', name: 'Seats', dataType: 'Number' } },
      ],
    });
    const caps = [makeCap('c1', 10, 'Number', 'Seats')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  // ── Boolean type ──

  it('returns no conflict when boolean capability is true and required', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: true, criterion: { id: 'c1', name: 'WiFi', dataType: 'Boolean' } },
      ],
    });
    const caps = [makeCap('c1', true, 'Boolean', 'WiFi')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  it('returns conflict when boolean capability is false but required', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: true, criterion: { id: 'c1', name: 'WiFi', dataType: 'Boolean' } },
      ],
    });
    const caps = [makeCap('c1', false, 'Boolean', 'WiFi')];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(1);
    expect(result[0].kind).toBe('connector_mismatch');
  });

  it('returns no conflict when boolean is not required', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: false, criterion: { id: 'c1', name: 'WiFi', dataType: 'Boolean' } },
      ],
    });
    const caps = [makeCap('c1', false, 'Boolean', 'WiFi')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  // ── String type ──

  it('returns no conflict when string values match', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 'HDMI', criterion: { id: 'c1', name: 'Connector', dataType: 'String' } },
      ],
    });
    const caps = [makeCap('c1', 'HDMI', 'String', 'Connector')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  it('returns warning when string values differ', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 'HDMI', criterion: { id: 'c1', name: 'Connector', dataType: 'String' } },
      ],
    });
    const caps = [makeCap('c1', 'VGA', 'String', 'Connector')];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(1);
    expect(result[0].severity).toBe('warning');
  });

  // ── Enum type ──

  it('returns no conflict when enum values match', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 'Large', criterion: { id: 'c1', name: 'Size', dataType: 'Enum' } },
      ],
    });
    const caps = [makeCap('c1', 'Large', 'Enum', 'Size')];
    expect(validateSpaceRequirements(request, caps)).toEqual([]);
  });

  it('returns error when enum values differ', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 'Large', criterion: { id: 'c1', name: 'Size', dataType: 'Enum' } },
      ],
    });
    const caps = [makeCap('c1', 'Small', 'Enum', 'Size')];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(1);
    expect(result[0].kind).toBe('size_mismatch');
    expect(result[0].severity).toBe('error');
  });

  // ── Multiple requirements ──

  it('validates multiple requirements and returns all conflicts', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 20, criterion: { id: 'c1', name: 'Seats', dataType: 'Number' } },
        { id: 'r2', requestId: 'req-1', criterionId: 'c2', value: true, criterion: { id: 'c2', name: 'WiFi', dataType: 'Boolean' } },
        { id: 'r3', requestId: 'req-1', criterionId: 'c3', value: 'HDMI', criterion: { id: 'c3', name: 'Connector', dataType: 'String' } },
      ],
    });
    const caps = [
      makeCap('c1', 10, 'Number', 'Seats'),     // insufficient
      makeCap('c2', false, 'Boolean', 'WiFi'),    // missing
      makeCap('c3', 'HDMI', 'String', 'Connector'), // matches
    ];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(2);
  });

  // ── Case normalization ──

  it('handles lowercase dataType (e.g. "number")', () => {
    const request = makeRequest({
      requirements: [
        { id: 'r1', requestId: 'req-1', criterionId: 'c1', value: 10, criterion: { id: 'c1', name: 'Seats', dataType: 'number' } },
      ],
    });
    const caps = [makeCap('c1', 5, 'number', 'Seats')];
    const result = validateSpaceRequirements(request, caps);
    expect(result).toHaveLength(1);
    expect(result[0].kind).toBe('load_exceeded');
  });
});

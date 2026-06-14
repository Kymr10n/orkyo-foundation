import { describe, it, expect, vi, beforeEach } from 'vitest';
import type {
  ValidationResult,
  ValidationIssue,
  ValidateResourceAssignmentRequest,
} from './resource-assignments-api';

const apiMocks = vi.hoisted(() => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiDelete: vi.fn(),
}));
vi.mock('../core/api-client', () => ({
  apiGet: apiMocks.apiGet,
  apiPost: apiMocks.apiPost,
  apiDelete: apiMocks.apiDelete,
}));

import {
  hardBlockers,
  softBlockers,
  getAssignmentsByResource,
  getAssignmentsByResourceType,
  getAssignmentsByRequest,
  validateAssignment,
  validateAssignmentsBatch,
  createAssignment,
  cancelAssignment,
} from './resource-assignments-api';

const issue = (code: ValidationIssue['code']): ValidationIssue => ({
  code,
  message: code,
});

const result = (blockers: ValidationIssue[]): ValidationResult => ({
  severity: 'blocker',
  blockers,
  warnings: [],
});

beforeEach(() => {
  vi.clearAllMocks();
  apiMocks.apiGet.mockResolvedValue([]);
  apiMocks.apiPost.mockResolvedValue({});
  apiMocks.apiDelete.mockResolvedValue(undefined);
});

describe('hardBlockers / softBlockers', () => {
  it('splits blockers into hard (genuine) and soft (warning) sets', () => {
    const blockers = [
      issue('offtime.overlap'), // hard
      issue('capability.missing'), // soft
      issue('assignment.overbooked'), // soft
      issue('site.cross-not-allowed'), // hard
    ];
    const r = result(blockers);

    expect(hardBlockers(r).map((b) => b.code)).toEqual([
      'offtime.overlap',
      'site.cross-not-allowed',
    ]);
    expect(softBlockers(r).map((b) => b.code)).toEqual([
      'capability.missing',
      'assignment.overbooked',
    ]);
  });

  it('returns empty arrays when there are no blockers', () => {
    const r = result([]);
    expect(hardBlockers(r)).toEqual([]);
    expect(softBlockers(r)).toEqual([]);
  });
});

describe('assignment queries', () => {
  it('builds a windowed query for a single resource', async () => {
    await getAssignmentsByResource(
      'res-1',
      new Date('2024-01-01T00:00:00Z'),
      new Date('2024-01-02T00:00:00Z'),
    );
    const url = apiMocks.apiGet.mock.calls[0][0] as string;
    expect(url).toContain('from=2024-01-01T00%3A00%3A00.000Z');
    expect(url).toContain('to=2024-01-02T00%3A00%3A00.000Z');
  });

  it('queries all resources of a type in one round-trip', async () => {
    await getAssignmentsByResourceType(
      'person',
      new Date('2024-01-01T00:00:00Z'),
      new Date('2024-01-02T00:00:00Z'),
    );
    const url = apiMocks.apiGet.mock.calls[0][0] as string;
    expect(url).toContain('resourceTypeKey=person');
  });

  it('queries assignments for a request by id', async () => {
    await getAssignmentsByRequest('req 1');
    const url = apiMocks.apiGet.mock.calls[0][0] as string;
    expect(url).toContain('requestId=req%201');
  });
});

describe('validation', () => {
  it('posts a single validation request', async () => {
    const req: ValidateResourceAssignmentRequest = {
      resourceId: 'res-1',
      startUtc: '2024-01-01T00:00:00Z',
      endUtc: '2024-01-01T01:00:00Z',
    };
    await validateAssignment(req);
    expect(apiMocks.apiPost).toHaveBeenCalledWith(expect.any(String), req);
  });

  it('short-circuits an empty batch without a network call', async () => {
    const out = await validateAssignmentsBatch([]);
    expect(out).toEqual([]);
    expect(apiMocks.apiPost).not.toHaveBeenCalled();
  });

  it('posts a non-empty batch wrapped under items', async () => {
    const items: ValidateResourceAssignmentRequest[] = [
      { resourceId: 'res-1', startUtc: 'a', endUtc: 'b' },
    ];
    await validateAssignmentsBatch(items);
    expect(apiMocks.apiPost).toHaveBeenCalledWith(expect.any(String), { items });
  });
});

describe('mutations', () => {
  it('creates an assignment via POST', async () => {
    const req = {
      requestId: 'req-1',
      resourceId: 'res-1',
      startUtc: 'a',
      endUtc: 'b',
    };
    await createAssignment(req);
    expect(apiMocks.apiPost).toHaveBeenCalledWith(expect.any(String), req);
  });

  it('cancels an assignment via DELETE', async () => {
    await cancelAssignment('asg-1');
    expect(apiMocks.apiDelete).toHaveBeenCalledWith(expect.stringContaining('asg-1'));
  });
});

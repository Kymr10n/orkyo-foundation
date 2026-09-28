import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import { pagedResult } from '@foundation/src/test-utils/paged-result';
import { getResources } from '@foundation/src/lib/api/resources-api';
import { useSpreadsheetImport } from './useSpreadsheetImport';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
  createResource: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/request-api', () => ({
  createRequest: vi.fn(),
}));

function renderImporter() {
  return renderHook(() => useSpreadsheetImport(), { wrapper: createTestQueryWrapper() }).result;
}

beforeEach(() => {
  vi.mocked(getResources).mockReset();
});

describe('useSpreadsheetImport.loadExistingCodes', () => {
  it('maps the site\'s coded resources by code', async () => {
    vi.mocked(getResources).mockResolvedValue(
      pagedResult([
        { id: 'r-1', code: 'WS-1' },
        { id: 'r-2', code: null },
      ] as never),
    );
    const result = renderImporter();

    const codes = await result.current.loadExistingCodes('site-1');

    expect(getResources).toHaveBeenCalledWith({ hasGeometry: true, isActive: true, siteId: 'site-1' });
    expect([...codes.entries()]).toEqual([['WS-1', 'r-1']]);
  });

  it('stops the import when the list was cut at the backend cap', async () => {
    vi.mocked(getResources).mockResolvedValue(
      pagedResult([{ id: 'r-1', code: 'WS-1' }] as never, { totalItems: 1200 }),
    );
    const result = renderImporter();

    await expect(result.current.loadExistingCodes('site-1')).rejects.toThrow(
      /1200 placeable resources.*first 1.*stopped to avoid duplicates/,
    );
  });
});

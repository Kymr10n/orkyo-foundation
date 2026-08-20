import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../core/api-client', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiPut: vi.fn(),
  apiDelete: vi.fn(),
}));

import { apiGet } from '../core/api-client';
import { getListDefinitions, LIST_COLUMN_DATA_TYPES, listColumnDataTypeLabel } from './lists-api';

describe('getListDefinitions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockResolvedValue([]);
  });

  it('asks for the bare collection when neither filter is set', async () => {
    // No trailing "?" — an empty query string is not a filter, and the URL is a cache key.
    await getListDefinitions();

    expect(apiGet).toHaveBeenCalledWith('/api/list-definitions');
  });

  it('narrows to a scope', async () => {
    await getListDefinitions(false, 'organization');

    expect(apiGet).toHaveBeenCalledWith('/api/list-definitions?scope=organization');
  });

  it('asks for retired definitions too', async () => {
    await getListDefinitions(true);

    expect(apiGet).toHaveBeenCalledWith('/api/list-definitions?includeInactive=true');
  });

  it('combines both filters', async () => {
    await getListDefinitions(true, 'resource');

    expect(apiGet).toHaveBeenCalledWith(
      '/api/list-definitions?includeInactive=true&scope=resource',
    );
  });
});

describe('LIST_COLUMN_DATA_TYPES', () => {
  it('names every type the column dialog can offer, row_ref included', () => {
    // The picker maps over this array, so a type missing here cannot be created at all.
    expect(LIST_COLUMN_DATA_TYPES.map((t) => t.value)).toEqual([
      'text',
      'number',
      'boolean',
      'date',
      'url',
      'select',
      'row_ref',
    ]);
  });

  it('labels a known type, and falls back to the raw value for an unknown one', () => {
    expect(listColumnDataTypeLabel('row_ref')).toBe('Row of this list');
    // A type the server knows and this build does not reads as itself rather than as blank.
    expect(listColumnDataTypeLabel('geo' as never)).toBe('geo');
  });
});

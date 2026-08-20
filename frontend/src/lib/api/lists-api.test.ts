import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../core/api-client', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiPut: vi.fn(),
  apiDelete: vi.fn(),
}));

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import {
  createListColumn,
  createListDefinition,
  createListRow,
  createSharedListInstance,
  deleteListColumn,
  deleteListDefinition,
  deleteListRow,
  deleteSharedListInstance,
  ensureResourceListInstance,
  getListDefinition,
  getListDefinitions,
  getListInstance,
  getListRows,
  getResourceListInstance,
  getSharedListInstances,
  LIST_COLUMN_DATA_TYPES,
  listColumnDataTypeLabel,
  updateListColumn,
  updateListDefinition,
  updateListRow,
  updateSharedListInstance,
} from './lists-api';

const DEF = 'def-1';
const COL = 'col-1';
const INST = 'inst-1';
const ROW = 'row-1';

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

/**
 * The routes, end to end.
 *
 * Every one of these is a contract with a backend endpoint: a wrong path is a 404 the UI reports
 * as an unexplained failure, and nothing else in the frontend would catch it. The definitions,
 * columns and instances layers each nest under the one above, which is where a path is easiest to
 * get subtly wrong.
 */
describe('lists-api routes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockResolvedValue([]);
    vi.mocked(apiPost).mockResolvedValue({});
    vi.mocked(apiPut).mockResolvedValue({});
    vi.mocked(apiDelete).mockResolvedValue(undefined);
  });

  it('addresses one definition', async () => {
    await getListDefinition(DEF);
    expect(apiGet).toHaveBeenCalledWith(`/api/list-definitions/${DEF}`);
  });

  it('creates, updates and deletes a definition', async () => {
    await createListDefinition({ name: 'Departments' });
    expect(apiPost).toHaveBeenCalledWith('/api/list-definitions', { name: 'Departments' });

    await updateListDefinition(DEF, { name: 'Units' });
    expect(apiPut).toHaveBeenCalledWith(`/api/list-definitions/${DEF}`, { name: 'Units' });

    await deleteListDefinition(DEF);
    expect(apiDelete).toHaveBeenCalledWith(`/api/list-definitions/${DEF}`);
  });

  it('nests columns under their definition', async () => {
    await createListColumn(DEF, { key: 'name', label: 'Name', dataType: 'text' });
    expect(apiPost).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/columns`, {
      key: 'name',
      label: 'Name',
      dataType: 'text',
    });

    await updateListColumn(DEF, COL, { label: 'Renamed' });
    expect(apiPut).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/columns/${COL}`, {
      label: 'Renamed',
    });

    await deleteListColumn(DEF, COL);
    expect(apiDelete).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/columns/${COL}`);
  });

  it('nests shared instances under their definition', async () => {
    await getSharedListInstances(DEF);
    expect(apiGet).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/instances`);

    await createSharedListInstance(DEF, { name: 'Departments' });
    expect(apiPost).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/instances`, {
      name: 'Departments',
    });

    await updateSharedListInstance(DEF, INST, { name: 'Units' });
    expect(apiPut).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/instances/${INST}`, {
      name: 'Units',
    });

    await deleteSharedListInstance(DEF, INST);
    expect(apiDelete).toHaveBeenCalledWith(`/api/list-definitions/${DEF}/instances/${INST}`);
  });

  it('addresses rows by instance, not by definition', async () => {
    // Rows hang off the instance that holds them; routing them under the definition would write
    // one instance's rows into another's.
    await getListRows(INST);
    expect(apiGet).toHaveBeenCalledWith(`/api/list-instances/${INST}/rows`);

    await createListRow(INST, { values: { name: 'Quality' } });
    expect(apiPost).toHaveBeenCalledWith(`/api/list-instances/${INST}/rows`, {
      values: { name: 'Quality' },
    });

    await updateListRow(INST, ROW, { values: { name: 'Quality North' } });
    expect(apiPut).toHaveBeenCalledWith(`/api/list-instances/${INST}/rows/${ROW}`, {
      values: { name: 'Quality North' },
    });

    await deleteListRow(INST, ROW);
    expect(apiDelete).toHaveBeenCalledWith(`/api/list-instances/${INST}/rows/${ROW}`);
  });

  it('reads one instance on its own', async () => {
    await getListInstance(INST);
    expect(apiGet).toHaveBeenCalledWith(`/api/list-instances/${INST}`);
  });

  it('resolves a per-resource list without creating it', async () => {
    // A read must never bring a holder into existence, or opening a resource would leave a trail
    // of empty instances behind every list field on the form.
    vi.mocked(apiGet).mockResolvedValue(null);

    await expect(getResourceListInstance('res-1', 'field-1')).resolves.toBeNull();
    expect(apiGet).toHaveBeenCalledWith('/api/resources/res-1/list-fields/field-1/instance');
    expect(apiPost).not.toHaveBeenCalled();
  });

  it('creates the per-resource holder only when asked to', async () => {
    await ensureResourceListInstance('res-1', 'field-1');
    expect(apiPost).toHaveBeenCalledWith(
      '/api/resources/res-1/list-fields/field-1/instance',
      undefined,
    );
  });
});

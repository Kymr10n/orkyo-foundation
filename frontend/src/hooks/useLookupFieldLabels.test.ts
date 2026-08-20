/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { useLookupFieldLabels } from './useLookupFieldLabels';

const getResourceCustomFields = vi.fn();
const getListRows = vi.fn();
const getListInstance = vi.fn();
const getListDefinition = vi.fn();

vi.mock('@foundation/src/lib/api/resource-custom-fields-api', () => ({
  getResourceCustomFields: (...a: unknown[]) => getResourceCustomFields(...a),
}));
vi.mock('@foundation/src/lib/api/lists-api', () => ({
  getListRows: (...a: unknown[]) => getListRows(...a),
  getListInstance: (...a: unknown[]) => getListInstance(...a),
  getListDefinition: (...a: unknown[]) => getListDefinition(...a),
}));

const TYPE_ID = 'type-1';
const INSTANCE_ID = 'inst-1';
const DEFINITION_ID = 'def-1';

function field(overrides: Record<string, unknown> = {}) {
  return {
    id: 'field-1',
    resourceTypeId: TYPE_ID,
    key: 'department',
    label: 'Department',
    dataType: 'list_lookup',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    listInstanceId: INSTANCE_ID,
    ...overrides,
  };
}

function resource(id: string, customFields: Record<string, unknown> | null) {
  return { id, name: id, resourceTypeKey: 'person', customFields } as never;
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
}

/**
 * A lookup value is stored as row ids. Everything that shows one outside a form — the resource
 * list's columns, the utilization grid's row labels — goes through this hook to turn those ids
 * into words, so a mistake here is a column of blanks or, worse, the wrong name.
 */
describe('useLookupFieldLabels', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getResourceCustomFields.mockResolvedValue([field()]);
    getListInstance.mockResolvedValue({ id: INSTANCE_ID, listDefinitionId: DEFINITION_ID });
    getListDefinition.mockResolvedValue({
      id: DEFINITION_ID,
      name: 'Departments',
      displayColumnId: 'col-name',
      columns: [
        { id: 'col-name', key: 'name', label: 'Name', dataType: 'text', isActive: true },
        { id: 'col-code', key: 'code', label: 'Code', dataType: 'text', isActive: true },
      ],
    });
    getListRows.mockResolvedValue([
      { id: 'row-a', values: { name: 'Quality', code: 'QA' } },
      { id: 'row-b', values: { name: 'Logistics', code: 'LOG' } },
    ]);
  });

  const render = (resources: unknown[], fieldKeys?: string[]) =>
    renderHook(
      () => useLookupFieldLabels(TYPE_ID, resources as never, fieldKeys),
      { wrapper },
    );

  it('names the row a resource points at', async () => {
    const { result } = render([resource('r1', { department: ['row-a'] })]);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'Quality' }));
  });

  it('uses the designated display column, not the first one', async () => {
    getListDefinition.mockResolvedValue({
      id: DEFINITION_ID,
      displayColumnId: 'col-code',
      columns: [
        { id: 'col-name', key: 'name', label: 'Name', dataType: 'text', isActive: true },
        { id: 'col-code', key: 'code', label: 'Code', dataType: 'text', isActive: true },
      ],
    });

    const { result } = render([resource('r1', { department: ['row-a'] })]);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'QA' }));
  });

  it('falls back to the first active column when nothing is designated', async () => {
    getListDefinition.mockResolvedValue({
      id: DEFINITION_ID,
      displayColumnId: null,
      columns: [{ id: 'col-name', key: 'name', label: 'Name', dataType: 'text', isActive: true }],
    });

    const { result } = render([resource('r1', { department: ['row-a'] })]);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'Quality' }));
  });

  it('joins several picked rows into one cell', async () => {
    const { result } = render([resource('r1', { department: ['row-a', 'row-b'] })]);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'Quality, Logistics' }));
  });

  it('drops an id that resolves to no row', async () => {
    // The row was deleted under us. A dangling id must not surface as a raw uuid.
    const { result } = render([resource('r1', { department: ['row-a', 'row-gone'] })]);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'Quality' }));
  });

  it('omits a resource whose lookup resolves to nothing at all', async () => {
    const { result } = render([resource('r1', { department: ['row-gone'] })]);

    await waitFor(() => expect(getListRows).toHaveBeenCalled());
    expect(result.current.r1).toBeUndefined();
  });

  it('ignores resources with no custom fields', async () => {
    const { result } = render([resource('r1', null)]);

    await waitFor(() => expect(getListRows).toHaveBeenCalled());
    expect(result.current).toEqual({});
  });

  it('narrows to the field keys the caller renders', async () => {
    getResourceCustomFields.mockResolvedValue([
      field(),
      field({ id: 'field-2', key: 'job_title', listInstanceId: 'inst-2' }),
    ]);

    const { result } = render([resource('r1', { department: ['row-a'] })], ['department']);

    await waitFor(() => expect(result.current.r1).toEqual({ department: 'Quality' }));
    // Only the requested field's instance is read; the other lookup costs nothing.
    expect(getListRows).toHaveBeenCalledTimes(1);
    expect(getListRows).toHaveBeenCalledWith(INSTANCE_ID);
  });

  it('skips inactive and non-lookup fields', async () => {
    getResourceCustomFields.mockResolvedValue([
      field({ isActive: false }),
      field({ id: 'field-3', key: 'note', dataType: 'text', listInstanceId: null }),
    ]);

    const { result } = render([resource('r1', { department: ['row-a'] })]);

    await waitFor(() => expect(getResourceCustomFields).toHaveBeenCalled());
    expect(getListRows).not.toHaveBeenCalled();
    expect(result.current).toEqual({});
  });

  it('asks for nothing until the resource type is known', () => {
    renderHook(() => useLookupFieldLabels(undefined, [], undefined), { wrapper });

    expect(getResourceCustomFields).not.toHaveBeenCalled();
  });
});

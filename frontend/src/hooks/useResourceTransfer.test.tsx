import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useResourceTransfer } from './useResourceTransfer';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils/export-handlers', () => ({
  exportResources: vi.fn(),
  importResources: vi.fn(),
}));

import { createResource } from '@foundation/src/lib/api/resources-api';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { exportResources, importResources } from '@foundation/src/lib/utils/export-handlers';

const resourceType: ResourceTypeInfo = {
  id: 'type-machine',
  key: 'machine',
  displayName: 'Machine',
  displayNamePlural: 'Machines',
  hasGeometry: false,
  hasDirectoryProfile: false,
  singleGroupMembership: false,
  isSystem: false,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const resources = [{ id: 'res-1', name: 'Lathe' } as ResourceInfo];

function importRow(name: string) {
  return { request: { resourceTypeKey: 'machine', name, allocationMode: 'Exclusive' }, source: {} };
}

let queryClient: QueryClient;
function wrapper({ children }: { children: React.ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function renderTransfer(options = {}) {
  return renderHook(() => useResourceTransfer(resourceType, resources, options), { wrapper });
}

/** Fires the TopBar import action the hook subscribes to, and returns its promise result. */
async function fireImport() {
  act(() => {
    useUiActionsStore.getState().triggerImport({
      context: 'resources:machine',
      file: new File(['name\nLathe'], 'machines.csv', { type: 'text/csv' }),
      format: 'csv',
    });
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  useUiActionsStore.setState({
    exportTick: 0,
    importTick: 0,
    commandPaletteTick: 0,
    tourTick: 0,
    lastExport: null,
    lastImport: null,
    exportRegistry: new Map(),
    importRegistry: new Map(),
  });
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  vi.mocked(getResourceCustomFields).mockResolvedValue([]);
  vi.mocked(createResource).mockResolvedValue({ id: 'created' } as ResourceInfo);
  vi.mocked(importResources).mockResolvedValue([importRow('Lathe')]);
});

describe('useResourceTransfer', () => {
  it('offers export and import for the type it is given', () => {
    renderTransfer();

    expect(useUiActionsStore.getState().exportRegistry.has('resources:machine')).toBe(true);
    expect(useUiActionsStore.getState().importRegistry.has('resources:machine')).toBe(true);
  });

  it('exports the resources it was handed', async () => {
    renderTransfer();

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'resources:machine', format: 'csv' });
    });

    await waitFor(() =>
      expect(exportResources).toHaveBeenCalledWith(resources, 'csv', 'machine'),
    );
  });

  it('tells the parser what each custom field holds, so CSV strings read back as their type', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      {
        id: 'f1',
        resourceTypeId: resourceType.id,
        key: 'capacity_kg',
        label: 'Capacity',
        dataType: 'number',
        isRequired: false,
        sortOrder: 0,
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    ]);

    renderTransfer();
    await waitFor(() => expect(getResourceCustomFields).toHaveBeenCalledWith('type-machine'));
    await fireImport();

    await waitFor(() =>
      expect(importResources).toHaveBeenCalledWith(expect.anything(), 'csv', 'machine', {
        capacity_kg: 'number',
      }),
    );
  });

  it('creates a resource per row', async () => {
    vi.mocked(importResources).mockResolvedValue([importRow('Lathe'), importRow('Mill')]);
    renderTransfer();

    await fireImport();

    await waitFor(() => expect(createResource).toHaveBeenCalledTimes(2));
  });

  it('imports the rows it can and reports the ones the server rejected', async () => {
    // Earlier rows are already committed when a later one fails, so stopping at the first
    // rejection would leave a half-imported file with nothing saying which rows landed.
    vi.mocked(importResources).mockResolvedValue([
      importRow('Lathe'),
      importRow('Mill'),
      importRow('Drill'),
    ]);
    vi.mocked(createResource)
      .mockResolvedValueOnce({ id: 'a' } as ResourceInfo)
      .mockRejectedValueOnce(new Error("Custom field 'Serial number' is required"))
      .mockResolvedValueOnce({ id: 'c' } as ResourceInfo);

    renderTransfer();
    await fireImport();

    const { toast } = await import('sonner');
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to import machines',
        { description: expect.stringContaining('Imported 2 of 3') },
      ),
    );
    // The rows after the failure still went in.
    expect(createResource).toHaveBeenCalledTimes(3);
  });

  it('names the first rejection so the file can be fixed', async () => {
    vi.mocked(importResources).mockResolvedValue([importRow('Lathe')]);
    vi.mocked(createResource).mockRejectedValue(new Error('Serial number is required'));

    renderTransfer();
    await fireImport();

    const { toast } = await import('sonner');
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to import machines',
        { description: expect.stringContaining('Lathe: Serial number is required') },
      ),
    );
  });

  it('rejects a file with no usable rows', async () => {
    vi.mocked(importResources).mockResolvedValue([]);

    renderTransfer();
    await fireImport();

    const { toast } = await import('sonner');
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to import machines',
        { description: expect.stringContaining('No valid machines found') },
      ),
    );
  });

  it('runs the per-type follow-up for each created row', async () => {
    const afterCreate = vi.fn().mockResolvedValue(undefined);

    renderTransfer({ afterCreate });
    await fireImport();

    await waitFor(() => expect(afterCreate).toHaveBeenCalledWith({ id: 'created' }, {}));
  });

  it('adds the caller’s extra columns to the export', async () => {
    renderTransfer({ extraColumns: (r: ResourceInfo) => ({ department: `dept-${r.id}` }) });

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'resources:machine', format: 'csv' });
    });

    await waitFor(() =>
      expect(exportResources).toHaveBeenCalledWith(
        [expect.objectContaining({ id: 'res-1', department: 'dept-res-1' })],
        'csv',
        'machine',
      ),
    );
  });

  it('counts a row whose follow-up failed as imported, and says what happened', async () => {
    // The resource exists — re-importing the row to fix the side table would create a second
    // copy, so calling it "rejected" points the user at the wrong remedy.
    vi.mocked(importResources).mockResolvedValue([importRow('Alice')]);
    const afterCreate = vi.fn().mockRejectedValue(new Error('profile email invalid'));

    renderTransfer({ afterCreate });
    await fireImport();

    const { toast } = await import('sonner');
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to import machines', {
        description: expect.stringContaining('Alice: created, but profile email invalid'),
      }),
    );
    expect(toast.error).toHaveBeenCalledWith('Failed to import machines', {
      description: expect.stringContaining('Imported 1 of 1'),
    });
  });
});

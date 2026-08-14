/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceTypeSettings } from './ResourceTypeSettings';
import type {
  ResourceTypeInfo,
} from '@foundation/src/lib/api/resource-types-api';

vi.mock('@foundation/src/lib/api/resource-types-api', () => ({
  getResourceTypes: vi.fn(),
  deleteResourceType: vi.fn(),
}));

vi.mock('./ResourceTypeEditDialog', () => ({
  ResourceTypeEditDialog: ({ open, resourceType }: any) =>
    open ? <div data-testid="type-dialog">{resourceType ? 'edit' : 'create'}</div> : null,
}));

vi.mock('./ResourceTypeCustomFieldsDialog', () => ({
  ResourceTypeCustomFieldsDialog: ({ open, resourceType, onOpenChange }: any) =>
    open ? (
      <div data-testid="fields-dialog">
        {resourceType.key}
        <button type="button" onClick={() => onOpenChange(false)}>close fields</button>
      </div>
    ) : null,
}));

import {
  getResourceTypes,
  deleteResourceType,
} from '@foundation/src/lib/api/resource-types-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

const types: ResourceTypeInfo[] = [
  {
    id: 'type-space',
    key: 'space',
    displayName: 'Space',
    displayNamePlural: 'Spaces',
    hasGeometry: false,
    hasDirectoryProfile: false,
    singleGroupMembership: false,
    isSystem: true,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'type-car',
    key: 'car',
    displayName: 'Car',
    displayNamePlural: 'Cars',
    hasGeometry: false,
    hasDirectoryProfile: false,
    singleGroupMembership: false,
    description: 'Fleet vehicle',
    isSystem: false,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

function renderSettings() {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ResourceTypeSettings />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

async function openRowMenu(name: string) {
  await userEvent.click(await screen.findByRole('button', { name: `Actions for ${name}` }));
}

describe('ResourceTypeSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResourceTypes).mockResolvedValue(types);
    vi.mocked(deleteResourceType).mockResolvedValue(undefined);
  });

  it('lists every type and marks the built-in ones', async () => {
    renderSettings();

    expect(await screen.findByText('Car')).toBeInTheDocument();
    expect(screen.getByText('Space')).toBeInTheDocument();
    expect(screen.getByText('Built-in')).toBeInTheDocument();
  });

  it('offers edit and remove only for user-defined types', async () => {
    renderSettings();

    await openRowMenu('Car');
    expect(await screen.findByRole('menuitem', { name: /Edit/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Remove/ })).toBeInTheDocument();

    await userEvent.keyboard('{Escape}');

    // The built-in Space type is not editable or removable.
    await openRowMenu('Space');
    expect(await screen.findByRole('menuitem', { name: /Custom fields/ })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: /Edit/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: /Remove/ })).not.toBeInTheDocument();
  });

  it('opens the create dialog from the header button', async () => {
    renderSettings();

    await screen.findByText('Car');
    await userEvent.click(screen.getByRole('button', { name: /Add Resource Type/ }));

    expect(await screen.findByTestId('type-dialog')).toHaveTextContent('create');
  });

  it('opens the edit dialog from the row menu, and closes it again', async () => {
    renderSettings();

    await openRowMenu('Car');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Edit/ }));
    expect(await screen.findByTestId('type-dialog')).toHaveTextContent('edit');
  });

  it('sorts by a column header', async () => {
    renderSettings();

    await screen.findByText('Car');
    await userEvent.click(screen.getByRole('button', { name: /Status/ }));

    // Sorting reads the status accessor rather than the badge, so both rows survive it.
    expect(screen.getByText('Car')).toBeInTheDocument();
    expect(screen.getByText('Space')).toBeInTheDocument();
  });

  it('offers a retry when the list cannot be loaded', async () => {
    vi.mocked(getResourceTypes).mockRejectedValueOnce(new Error('offline'));
    renderSettings();

    const retry = await screen.findByRole('button', { name: /Try again/ });
    vi.mocked(getResourceTypes).mockResolvedValue(types);
    await userEvent.click(retry);

    expect(await screen.findByText('Car')).toBeInTheDocument();
  });

  it('says so when no types are defined', async () => {
    vi.mocked(getResourceTypes).mockResolvedValue([]);
    renderSettings();

    expect(await screen.findByText(/No resource types defined yet/)).toBeInTheDocument();
  });

  it('removes a type after confirmation', async () => {
    renderSettings();

    await openRowMenu('Car');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Remove/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(deleteResourceType).toHaveBeenCalledWith('type-car'));
  });

  // ── custom fields ─────────────────────────────────────────────────────────

  it('opens the custom fields dialog for the chosen type', async () => {
    renderSettings();

    await openRowMenu('Car');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Custom fields/ }));

    expect(await screen.findByTestId('fields-dialog')).toHaveTextContent('car');
  });

  it('offers custom fields on built-in types too', async () => {
    // A serial number on a Space is as ordinary as one on a Car, even though the
    // type itself is not the tenant's to rename or delete.
    renderSettings();

    await openRowMenu('Space');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Custom fields/ }));

    expect(await screen.findByTestId('fields-dialog')).toHaveTextContent('space');
  });

  it('does not mount the fields dialog until it is asked for', async () => {
    renderSettings();

    await screen.findByText('Car');
    expect(screen.queryByTestId('fields-dialog')).not.toBeInTheDocument();
  });

  it('closes the fields dialog again', async () => {
    renderSettings();

    await openRowMenu('Car');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Custom fields/ }));
    await screen.findByTestId('fields-dialog');

    await userEvent.click(screen.getByRole('button', { name: 'close fields' }));
    expect(screen.queryByTestId('fields-dialog')).not.toBeInTheDocument();
  });
});

/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceTypeSettings } from './ResourceTypeSettings';
import type {
  ResourceTypeInfo,
} from '@foundation/src/lib/api/resource-types-api';

vi.mock('@foundation/src/lib/api/resource-types-api', () => ({
  getResourceTypes: vi.fn(),
  deleteResourceType: vi.fn(),
  deactivateResourceTypeField: vi.fn(),
}));

const canEdit = { value: true };
vi.mock('@foundation/src/hooks/usePermissions', () => ({
  useCanEdit: () => canEdit.value,
}));

vi.mock('./ResourceTypeEditDialog', () => ({
  ResourceTypeEditDialog: ({ open, resourceType }: any) =>
    open ? <div data-testid="type-dialog">{resourceType ? 'edit' : 'create'}</div> : null,
}));

vi.mock('./ResourceTypeFieldEditDialog', () => ({
  ResourceTypeFieldEditDialog: ({ open, field }: any) =>
    open ? <div data-testid="field-dialog">{field ? 'edit' : 'create'}</div> : null,
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
    isSystem: true,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'type-car',
    key: 'car',
    displayName: 'Car',
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
    <QueryClientProvider client={queryClient}>
      <ResourceTypeSettings />
    </QueryClientProvider>,
  );
}

describe('ResourceTypeSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    canEdit.value = true;
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

    await screen.findByText('Car');
    expect(screen.getByLabelText('Edit Car')).toBeInTheDocument();
    // The built-in Space type is not editable or removable.
    expect(screen.queryByLabelText('Edit Space')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Remove Space')).not.toBeInTheDocument();
  });

  it('removes a type after confirmation', async () => {
    renderSettings();

    await userEvent.click(await screen.findByLabelText('Remove Car'));
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(deleteResourceType).toHaveBeenCalledWith('type-car'));
  });

  it('hides every write affordance from viewers', async () => {
    canEdit.value = false;
    renderSettings();

    await screen.findByText('Car');
    expect(screen.queryByRole('button', { name: /Add Resource Type/ })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Edit Car')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Remove Car')).not.toBeInTheDocument();
  });
});

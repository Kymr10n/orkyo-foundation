/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceTypeCustomFieldsDialog } from './ResourceTypeCustomFieldsDialog';
import { machineResourceType, customField } from '@foundation/src/test-utils/resource-fixtures';

// Partial: the module also exports the shared data-type labels, which the UI renders.
vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
  deleteResourceCustomField: vi.fn(),
}));

vi.mock('./CustomFieldEditDialog', () => ({
  CustomFieldEditDialog: ({ open, field, resourceTypeId }: any) =>
    open ? (
      <div data-testid="field-dialog">
        {field ? `edit:${field.key}` : 'create'}:{resourceTypeId}
      </div>
    ) : null,
}));

import {
  getResourceCustomFields,
  deleteResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';
import { createTestQueryClient } from '@foundation/src/test-utils';

const resourceType = machineResourceType;

function renderDialog(open = true) {
  const { queryClient } = createTestQueryClient({ feedback: true });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ResourceTypeCustomFieldsDialog
          resourceType={resourceType}
          open={open}
          onOpenChange={() => {}}
        />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

async function openFieldMenu(label: string) {
  await userEvent.click(await screen.findByRole('button', { name: `Actions for ${label}` }));
}

describe('ResourceTypeCustomFieldsDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(deleteResourceCustomField).mockResolvedValue(undefined);
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      customField({ key: 'serial_number', label: 'Serial number', isRequired: true }),
      customField({ key: 'datasheet', label: 'Datasheet', dataType: 'url' }),
    ]);
  });

  it('titles itself after the type and lists its fields', async () => {
    renderDialog();

    expect(screen.getByText('Custom fields — Machines')).toBeInTheDocument();
    expect(await screen.findByText('Serial number')).toBeInTheDocument();
    expect(screen.getByText('Datasheet')).toBeInTheDocument();
    expect(getResourceCustomFields).toHaveBeenCalledWith('type-machine');
  });

  it('names the boundary against criteria so the two systems stay distinct', async () => {
    renderDialog();

    expect(await screen.findByText(/belongs in Criteria/)).toBeInTheDocument();
  });

  it('marks required and hidden fields', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      customField({ key: 'serial_number', label: 'Serial number', isRequired: true }),
      customField({ key: 'legacy', label: 'Legacy code', isActive: false }),
    ]);

    renderDialog();

    expect(await screen.findByText('Required')).toBeInTheDocument();
    expect(screen.getByText('Hidden')).toBeInTheDocument();
  });

  it('fetches nothing while closed', () => {
    renderDialog(false);

    expect(getResourceCustomFields).not.toHaveBeenCalled();
  });

  it('opens the create dialog for this type', async () => {
    renderDialog();

    await userEvent.click(await screen.findByRole('button', { name: /Add custom field/ }));

    expect(await screen.findByTestId('field-dialog')).toHaveTextContent('create:type-machine');
  });

  it('opens the edit dialog for a field', async () => {
    renderDialog();

    await openFieldMenu('Serial number');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Edit/ }));

    expect(await screen.findByTestId('field-dialog')).toHaveTextContent('edit:serial_number');
  });

  it('removes a field after confirmation', async () => {
    renderDialog();

    await openFieldMenu('Datasheet');
    await userEvent.click(await screen.findByRole('menuitem', { name: /Remove/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() =>
      expect(deleteResourceCustomField).toHaveBeenCalledWith('type-machine', 'field-datasheet'),
    );
  });

  it('says so when the type has no fields yet', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([]);

    renderDialog();

    expect(await screen.findByText(/No custom fields yet/)).toBeInTheDocument();
  });
});

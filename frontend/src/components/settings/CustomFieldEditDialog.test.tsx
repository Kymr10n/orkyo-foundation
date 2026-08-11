import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { CustomFieldEditDialog } from './CustomFieldEditDialog';
import type { ResourceCustomField } from '@foundation/src/lib/api/resource-custom-fields-api';

// Partial: the module also exports the shared data-type labels, which the select renders.
vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  createResourceCustomField: vi.fn(),
  updateResourceCustomField: vi.fn(),
}));

import {
  createResourceCustomField,
  updateResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

const existing: ResourceCustomField = {
  id: 'field-1',
  resourceTypeId: 'type-machine',
  key: 'serial_number',
  label: 'Serial number',
  description: 'Stamped on the frame',
  dataType: 'text',
  isRequired: false,
  sortOrder: 2,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderDialog(field: ResourceCustomField | null = null) {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <CustomFieldEditDialog
        resourceTypeId="type-machine"
        field={field}
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
}

const save = () => screen.getByRole('button', { name: 'Save' });

describe('CustomFieldEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createResourceCustomField).mockResolvedValue(existing);
    vi.mocked(updateResourceCustomField).mockResolvedValue(existing);
  });

  // ── creating ──────────────────────────────────────────────────────────────

  it('derives the key from the label so nobody has to invent one', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), 'Serial number');

    expect(screen.getByLabelText('Key')).toHaveAttribute('placeholder', 'serial_number');

    await userEvent.click(save());
    await waitFor(() =>
      expect(createResourceCustomField).toHaveBeenCalledWith(
        'type-machine',
        expect.objectContaining({ key: 'serial_number', label: 'Serial number', dataType: 'text' }),
      ),
    );
  });

  it('prefixes a derived key that would start with a digit', async () => {
    // The key format demands a leading letter, and "2026 audit" is a plausible label.
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), '2026 audit');
    await userEvent.click(save());

    await waitFor(() =>
      expect(createResourceCustomField).toHaveBeenCalledWith(
        'type-machine',
        expect.objectContaining({ key: 'f_2026_audit' }),
      ),
    );
  });

  it('explains the key format when a label yields no usable key', async () => {
    // "***" has nothing to derive from, so Save is blocked — it must say why.
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), '***');

    expect(save()).toBeDisabled();
    expect(screen.getByText(/must start with a letter/i)).toBeInTheDocument();
  });

  it('explains the key format when the typed key is malformed', async () => {
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), 'Serial number');
    await userEvent.type(screen.getByLabelText('Key'), 'Serial-Number');

    expect(save()).toBeDisabled();
    expect(screen.getByText(/must start with a letter/i)).toBeInTheDocument();
  });

  it('keeps Save disabled until a label is given', async () => {
    renderDialog();

    expect(save()).toBeDisabled();

    await userEvent.type(screen.getByLabelText('Label'), 'Serial number');
    await waitFor(() => expect(save()).toBeEnabled());
  });

  it('describes the chosen data type', async () => {
    renderDialog();

    expect(screen.getByText(/A short line of text/)).toBeInTheDocument();
  });

  it('warns that an existing resource will have to fill a required field in', async () => {
    renderDialog();

    await userEvent.click(screen.getByLabelText('Required'));

    expect(screen.getByText(/has to fill this in/)).toBeInTheDocument();
  });

  it('survives the order box being cleared mid-edit', async () => {
    // Number('') is 0, so a naive controlled number input rewrites the box to 0 as you retype.
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), 'Serial number');
    const order = screen.getByLabelText('Order');
    await userEvent.clear(order);

    expect(order).toHaveValue(null);

    await userEvent.type(order, '10');
    await userEvent.click(save());

    await waitFor(() =>
      expect(createResourceCustomField).toHaveBeenCalledWith(
        'type-machine',
        expect.objectContaining({ sortOrder: 10 }),
      ),
    );
  });

  // ── editing ───────────────────────────────────────────────────────────────

  it('hides key and type when editing, and says why', async () => {
    renderDialog(existing);

    expect(screen.queryByLabelText('Key')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Type')).not.toBeInTheDocument();
    expect(screen.getByText(/are fixed/)).toBeInTheDocument();
    // The type's label comes from the shared list, so it reads as the picker spelled it.
    expect(screen.getByText('Text')).toBeInTheDocument();
  });

  it('sends only what an edit may change', async () => {
    renderDialog(existing);

    await userEvent.clear(screen.getByLabelText('Label'));
    await userEvent.type(screen.getByLabelText('Label'), 'Serial no.');
    await userEvent.click(screen.getByLabelText('Required'));
    await userEvent.click(save());

    await waitFor(() =>
      expect(updateResourceCustomField).toHaveBeenCalledWith('type-machine', 'field-1', {
        label: 'Serial no.',
        description: 'Stamped on the frame',
        isRequired: true,
        sortOrder: 2,
        isActive: true,
      }),
    );
  });

  it('offers the shown-on-the-form toggle only when editing', async () => {
    // The confirm copy in the fields dialog points at this exact label.
    renderDialog(existing);
    expect(screen.getByLabelText('Shown on the form')).toBeInTheDocument();

    renderDialog();
    expect(screen.getAllByLabelText('Shown on the form')).toHaveLength(1);
  });

  it('surfaces a rejected save inline', async () => {
    vi.mocked(createResourceCustomField).mockRejectedValue(
      new Error("A custom field with key 'serial_number' already exists for this resource type"),
    );
    renderDialog();

    await userEvent.type(screen.getByLabelText('Label'), 'Serial number');
    await userEvent.click(save());

    expect(await screen.findByText(/already exists/)).toBeInTheDocument();
  });
});

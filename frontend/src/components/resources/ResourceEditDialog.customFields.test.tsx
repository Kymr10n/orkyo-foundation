import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceEditDialog } from './ResourceEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceCustomField } from '@foundation/src/lib/api/resource-custom-fields-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
  updateResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
}));

vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: [] }),
  useIsMultiSite: () => false,
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

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

function field(overrides: Partial<ResourceCustomField> & { key: string }): ResourceCustomField {
  return {
    id: `field-${overrides.key}`,
    resourceTypeId: resourceType.id,
    label: overrides.key,
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function renderDialog(resource: ResourceInfo | null = null) {
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceEditDialog
        resourceType={resourceType}
        resource={resource}
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
}

describe('ResourceEditDialog custom fields', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createResource).mockResolvedValue({ id: 'res-1' } as ResourceInfo);
    vi.mocked(updateResource).mockResolvedValue({ id: 'res-1' } as ResourceInfo);
  });

  it('renders the type’s active fields in sort order', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial_number', label: 'Serial number', sortOrder: 1 }),
      field({ key: 'datasheet', label: 'Datasheet', dataType: 'url', sortOrder: 2 }),
      field({ key: 'retired', label: 'Retired field', isActive: false, sortOrder: 3 }),
    ]);

    renderDialog();

    await screen.findByLabelText('Serial number');
    const labels = screen.getAllByText(/Serial number|Datasheet|Retired field/);
    expect(labels.map((l) => l.textContent)).toEqual(['Serial number', 'Datasheet']);
  });

  it('blocks submit until a required field is filled in', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial_number', label: 'Serial number', isRequired: true }),
    ]);

    renderDialog();

    await screen.findByLabelText(/Serial number/);
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');

    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/Serial number/), 'SN-42');
    await waitFor(() => expect(save).toBeEnabled());
  });

  it('sends the values with the create request', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial_number', label: 'Serial number' }),
    ]);

    renderDialog();

    await screen.findByLabelText('Serial number');
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    await userEvent.type(screen.getByLabelText('Serial number'), 'SN-42');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({ customFields: { serial_number: 'SN-42' } }),
      ),
    );
  });

  it('keeps values for retired fields when saving an edit', async () => {
    // A save replaces the whole document, so a value the form never showed would otherwise
    // be discarded the next time anyone renames the resource.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial_number', label: 'Serial number' }),
      field({ key: 'legacy_code', label: 'Legacy code', isActive: false }),
    ]);

    renderDialog({
      id: 'res-1',
      resourceTypeId: resourceType.id,
      resourceTypeKey: resourceType.key,
      name: 'Lathe',
      allocationMode: 'Exclusive',
      baseAvailabilityPercent: 100,
      isPhysical: false,
      capacity: 1,
      isActive: true,
      customFields: { serial_number: 'SN-42', legacy_code: 'OLD-7' },
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });

    await screen.findByLabelText('Serial number');
    await userEvent.clear(screen.getByLabelText('Name'));
    await userEvent.type(screen.getByLabelText('Name'), 'Renamed lathe');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(updateResource).toHaveBeenCalledWith(
        'res-1',
        expect.objectContaining({
          customFields: { serial_number: 'SN-42', legacy_code: 'OLD-7' },
        }),
      ),
    );
  });

  it('blocks submit until the definitions have loaded', async () => {
    // An unfinished load looks exactly like "this type has no required fields", so submitting
    // then would skip the check and come back as a raw 400 from the server.
    let resolve!: (fields: ResourceCustomField[]) => void;
    vi.mocked(getResourceCustomFields).mockReturnValue(
      new Promise<ResourceCustomField[]>((r) => { resolve = r; }),
    );

    renderDialog();

    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

    resolve([field({ key: 'notes', label: 'Notes' })]);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
  });

  it('says so — and refuses to save — when the definitions cannot be loaded', async () => {
    vi.mocked(getResourceCustomFields).mockRejectedValue(new Error('offline'));

    renderDialog();

    expect(await screen.findByText(/Could not load this type's custom fields/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('treats an untouched required checkbox as answered "no"', async () => {
    // A checkbox has no unfilled state to render, so requiring one to be ticked would disable
    // Save with nothing on screen to fix.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'certified', label: 'Certified', dataType: 'boolean', isRequired: true }),
    ]);

    renderDialog();

    await screen.findByRole('checkbox', { name: /Certified/ });
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({ customFields: { certified: false } }),
      ),
    );
  });

  it('does not send a field the user typed into and cleared again', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'notes', label: 'Notes' }),
    ]);

    renderDialog();

    const notes = await screen.findByLabelText('Notes');
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    await userEvent.type(notes, 'x');
    await userEvent.clear(notes);
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({ customFields: {} }),
      ),
    );
  });

  it('sends the built-in fields alongside the custom ones', async () => {
    // The custom-field section sits beside the standing fields; both go in one request.
    vi.mocked(getResourceCustomFields).mockResolvedValue([field({ key: 'notes', label: 'Notes' })]);

    renderDialog();

    await screen.findByLabelText('Notes');
    await userEvent.type(screen.getByLabelText('Name'), 'Lathe');
    await userEvent.type(screen.getByLabelText('Description'), 'Shop floor');
    await userEvent.type(screen.getByLabelText('External reference'), 'ERP-9');
    await userEvent.clear(screen.getByLabelText(/Base Availability/));
    await userEvent.type(screen.getByLabelText(/Base Availability/), '80');
    await userEvent.type(screen.getByLabelText('Notes'), 'Runs hot');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'Lathe',
          description: 'Shop floor',
          externalReference: 'ERP-9',
          baseAvailabilityPercent: 80,
          customFields: { notes: 'Runs hot' },
        }),
      ),
    );
  });

  it('shows no custom-field section when the type defines none', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([]);

    renderDialog();

    await screen.findByLabelText('Name');
    expect(screen.queryByText('Serial number')).not.toBeInTheDocument();
  });

  // ── placeable types ───────────────────────────────────────────────────────

  it('never claims a placeable resource can travel', async () => {
    // Regression: the form defaulted crossSiteAllowed to true for every type, and the server
    // rejects that for a placeable one — "belong to one site and cannot travel" — so creating
    // a resource of a tenant-defined placeable type always 400'd.
    vi.mocked(getResourceCustomFields).mockResolvedValue([]);
    const placeable = { ...resourceType, key: 'car', displayName: 'Car', displayNamePlural: 'Cars', hasGeometry: true };
    const { queryClient } = createFeedbackTestQueryClientWithSpy();
    render(
      <QueryClientProvider client={queryClient}>
        <ResourceEditDialog resourceType={placeable} resource={null} open onOpenChange={() => {}} />
      </QueryClientProvider>,
    );

    await userEvent.type(await screen.findByLabelText('Name'), 'Forklift 3');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({ crossSiteAllowed: false }),
      ),
    );
  });

  it('does not report a change when a value is edited and typed back', async () => {
    // Dirty-checking stringifies the whole form, so rewriting a key at the end of the document
    // would make an undone edit look permanently unsaved and trap the discard prompt.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial_number', label: 'Serial number' }),
      field({ key: 'notes', label: 'Notes' }),
    ]);

    renderDialog({
      id: 'res-1',
      resourceTypeId: resourceType.id,
      resourceTypeKey: resourceType.key,
      name: 'Lathe',
      allocationMode: 'Exclusive',
      baseAvailabilityPercent: 100,
      isPhysical: false,
      capacity: 1,
      isActive: true,
      customFields: { serial_number: 'SN-42', notes: 'runs hot' },
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });

    const serial = await screen.findByLabelText('Serial number');
    await userEvent.clear(serial);
    await userEvent.type(serial, 'SN-43');
    await userEvent.clear(serial);
    await userEvent.type(serial, 'SN-42');

    // Back to the stored document, so closing must not prompt to discard anything.
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
  });
});

describe('ResourceEditDialog width', () => {
  it('widens to fit a list field, which renders a whole data table', async () => {
    // The reported bug: a maintenance-log list inside the default form width scrolled sideways.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'maintenance', label: 'Maintenance log', dataType: 'list' }),
    ]);
    renderDialog();

    await waitFor(() =>
      expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-3xl'),
    );
  });

  it('keeps the default form width when every field is a plain input', async () => {
    // Widening unconditionally would strand single-column inputs across 720px.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      field({ key: 'serial', label: 'Serial', dataType: 'text' }),
    ]);
    renderDialog();

    await waitFor(() => expect(screen.getByLabelText(/Serial/)).toBeInTheDocument());
    expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-[500px]');
    expect(screen.getByRole('dialog')).not.toHaveClass('sm:max-w-3xl');
  });
});

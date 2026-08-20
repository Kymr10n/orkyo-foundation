import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ResourceList } from './ResourceList';
import { deleteResource, getResources } from '@foundation/src/lib/api/resources-api';

import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
  deleteResource: vi.fn(),
}));

// The directory columns read organization lookups through the shared resolver; its own suite
// covers the resolution, so here it is stubbed to whatever the case needs.
vi.mock('@foundation/src/hooks/useLookupFieldLabels', () => ({
  useLookupFieldLabels: (typeId: string | undefined) =>
    typeId === undefined ? {} : lookupLabels,
}));

vi.mock('@foundation/src/hooks/usePermissions', () => ({
  useCanEdit: () => true,
}));

// The dialogs own their data loading; stub them so this suite stays on row plumbing.
vi.mock('./ResourceEditDialog', () => ({
  ResourceEditDialog: ({ open, resource }: { open: boolean; resource: { id: string } | null }) =>
    open ? <div data-testid="resource-edit-dialog" data-resource-id={resource?.id ?? ''} /> : null,
}));

vi.mock('./ResourceCapabilitiesEditor', () => ({
  ResourceCapabilitiesEditor: ({
    open,
    resourceId,
    resourceTypeKey,
  }: {
    open: boolean;
    resourceId: string;
    resourceTypeKey: string;
  }) =>
    open ? (
      <div
        data-testid="capabilities-editor"
        data-resource-id={resourceId}
        data-type-key={resourceTypeKey}
      />
    ) : null,
}));

vi.mock('./ResourceAbsenceList', () => ({
  ResourceAbsenceList: ({ open, resourceId }: { open: boolean; resourceId: string }) =>
    open ? <div data-testid="absence-list" data-resource-id={resourceId} /> : null,
}));

// Mutable so a case can decide what the resolver returns before rendering.
let lookupLabels: Record<string, Record<string, string>> = {};

const carType: ResourceTypeInfo = {
  id: 'type-car',
  key: 'car',
  displayName: 'Car',
  displayNamePlural: 'Cars',
  hasGeometry: false,
  hasDirectoryProfile: false,
  singleGroupMembership: false,
  isSystem: false,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const cars = [
  { id: 'car-1', name: 'Van 1', resourceTypeKey: 'car', isActive: true },
  { id: 'car-2', name: 'Van 2', resourceTypeKey: 'car', isActive: false },
];

function renderList() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <ResourceList resourceType={carType} />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

/** Opens the row menu for a resource and clicks one of its actions. */
async function chooseAction(resourceName: string, action: RegExp) {
  await userEvent.click(await screen.findByRole('button', { name: `Actions for ${resourceName}` }));
  await userEvent.click(await screen.findByRole('menuitem', { name: action }));
}

const personType: ResourceTypeInfo = {
  ...carType,
  id: 'type-person',
  key: 'person',
  displayName: 'Person',
  displayNamePlural: 'People',
  hasDirectoryProfile: true,
  isSystem: true,
};

const people = [
  {
    id: 'p-1',
    name: 'Ada Heaney',
    resourceTypeKey: 'person',
    isActive: true,
    email: 'ada@example.com',
  },
  { id: 'p-2', name: 'John Smith', resourceTypeKey: 'person', isActive: true },
];

function renderPeople() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <ResourceList resourceType={personType} />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  // getResources returns a paged envelope, not a bare array.
  (getResources as Mock).mockResolvedValue({ data: cars, total: cars.length });
  lookupLabels = { 'p-1': { job_title: 'Machinist', department: 'Assembly' } };
});

describe('ResourceList', () => {
  it('lists the resources of its type and flags inactive ones', async () => {
    renderList();

    expect(await screen.findByText('Van 1')).toBeInTheDocument();
    expect(screen.getByText('Van 2')).toBeInTheDocument();
    // Only the deactivated one carries the badge.
    expect(screen.getAllByText('Inactive')).toHaveLength(1);
  });

  it('requests only its own resource type', async () => {
    renderList();

    await waitFor(() =>
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'car' }),
    );
  });

  it('labels row actions with the type display name, not "resource"', async () => {
    renderList();
    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Van 1' }));

    expect(await screen.findByRole('menuitem', { name: /Edit Car/ })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Deactivate Car/ })).toBeInTheDocument();
  });

  // The three below are the parity this phase exists to deliver: a tenant-defined type
  // gets the same per-row reach as the built-in Spaces and People pages.
  it('opens the capabilities editor for the chosen resource', async () => {
    renderList();
    await chooseAction('Van 1', /Manage Capabilities/);

    const editor = await screen.findByTestId('capabilities-editor');
    expect(editor).toHaveAttribute('data-resource-id', 'car-1');
    // Scoped to the type, so only car-applicable criteria are offered.
    expect(editor).toHaveAttribute('data-type-key', 'car');
  });

  it('opens the absence list for the chosen resource', async () => {
    renderList();
    await chooseAction('Van 2', /Manage Absences/);

    expect(await screen.findByTestId('absence-list')).toHaveAttribute('data-resource-id', 'car-2');
  });

  it('opens the edit dialog for the chosen resource', async () => {
    renderList();
    await chooseAction('Van 1', /Edit Car/);

    expect(await screen.findByTestId('resource-edit-dialog')).toHaveAttribute(
      'data-resource-id',
      'car-1',
    );
  });

  it('deactivates only after confirmation', async () => {
    (deleteResource as Mock).mockResolvedValue(undefined);
    renderList();

    await chooseAction('Van 1', /Deactivate Car/);
    expect(deleteResource).not.toHaveBeenCalled();

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }));
    await waitFor(() => expect(deleteResource).toHaveBeenCalledWith('car-1'));
  });

  it('opens the edit dialog when the row itself is clicked', async () => {
    renderList();
    await userEvent.click(await screen.findByText('Van 1'));

    expect(await screen.findByTestId('resource-edit-dialog')).toHaveAttribute(
      'data-resource-id',
      'car-1',
    );
  });

  it('does not open the edit dialog when the row action menu is used', async () => {
    // The row opens the editor, so the actions cell must stop propagation — otherwise
    // choosing Deactivate would leave the edit dialog open behind the confirm.
    renderList();
    await chooseAction('Van 1', /Manage Absences/);

    expect(await screen.findByTestId('absence-list')).toBeInTheDocument();
    expect(screen.queryByTestId('resource-edit-dialog')).not.toBeInTheDocument();
  });

});

// The person merge: what used to be PersonList's job is the directory flag on the type.
describe('ResourceList — directory types', () => {
  it('shows no directory columns for a type without a directory profile', async () => {
    renderList();

    expect(await screen.findByText('Van 1')).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: /email/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: /job title/i })).not.toBeInTheDocument();
  });

  it('shows email, job title and department for a directory type', async () => {
    (getResources as Mock).mockResolvedValue({ data: people, total: people.length });
    renderPeople();

    expect(await screen.findByText('Ada Heaney')).toBeInTheDocument();
    expect(screen.getByText('ada@example.com')).toBeInTheDocument();
    // Resolved from the id through the lookups, so the column reads as a name.
    expect(await screen.findByText('Machinist')).toBeInTheDocument();
    expect(screen.getByText('Assembly')).toBeInTheDocument();
  });

  it('renders a dash for a person with no directory values', async () => {
    (getResources as Mock).mockResolvedValue({ data: [people[1]], total: 1 });
    renderPeople();

    expect(await screen.findByText('John Smith')).toBeInTheDocument();
    // Email, job title and department all fall back rather than rendering an id.
    expect(screen.getAllByText('-').length).toBeGreaterThanOrEqual(3);
  });

  it('calls criterion values "Skills" for people', async () => {
    (getResources as Mock).mockResolvedValue({ data: people, total: people.length });
    renderPeople();

    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Ada Heaney' }));
    expect(await screen.findByRole('menuitem', { name: /Manage Skills/ })).toBeInTheDocument();
  });

  it('calls them "Capabilities" for every other type', async () => {
    renderList();

    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Van 1' }));
    expect(await screen.findByRole('menuitem', { name: /Manage Capabilities/ })).toBeInTheDocument();
  });
});

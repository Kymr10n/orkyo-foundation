import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { TypeCatalogSettings } from './TypeCatalogSettings';
import type { CatalogEntry } from '@foundation/src/lib/api/resource-type-catalog-api';

vi.mock('@foundation/src/lib/api/resource-type-catalog-api', () => ({
  getResourceTypeCatalog: vi.fn(),
  activateCatalogType: vi.fn(),
  deactivateCatalogType: vi.fn(),
  purgeCatalogType: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import {
  getResourceTypeCatalog,
  activateCatalogType,
  deactivateCatalogType,
  purgeCatalogType,
} from '@foundation/src/lib/api/resource-type-catalog-api';
import { toast } from 'sonner';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';
import { qk } from '@foundation/src/lib/api/query-keys';

const entry = (overrides: Partial<CatalogEntry>): CatalogEntry => ({
  key: 'drill',
  displayName: 'Drill',
  displayNamePlural: 'Drills',
  description: 'Drill presses and pillar drills.',
  icon: 'Drill',
  category: 'Stationary',
  hasGeometry: true,
  hasDirectoryProfile: false,
  singleGroupMembership: true,
  fieldLabels: ['a', 'b', 'c', 'd', 'e'],
  state: 'absent',
  resourceTypeId: null,
  tenantDisplayName: null,
  resourceCount: 0,
  requestTargetCount: 0,
  ...overrides,
});

const catalog: CatalogEntry[] = [
  entry({}),
  entry({
    key: 'lathe',
    displayName: 'Lathe',
    displayNamePlural: 'Lathes',
    description: 'Turning machines.',
    icon: 'Cylinder',
    state: 'inactive',
    resourceTypeId: 'type-lathe',
  }),
  entry({
    key: 'person',
    displayName: 'Person',
    displayNamePlural: 'People',
    description: 'Operators and staff.',
    icon: 'Users',
    category: 'Mobile',
    hasGeometry: false,
    singleGroupMembership: false,
    hasDirectoryProfile: true,
    fieldLabels: ['badge'],
    state: 'active',
    resourceTypeId: 'type-person',
    tenantDisplayName: 'Mitarbeiter',
    resourceCount: 4,
    requestTargetCount: 2,
  }),
  entry({
    key: 'tool',
    displayName: 'Tool',
    displayNamePlural: 'Tools',
    description: 'Hand and measuring tools.',
    icon: 'Wrench',
    category: 'Mobile',
    hasGeometry: false,
    singleGroupMembership: false,
    fieldLabels: ['serial'],
    state: 'active',
    resourceTypeId: 'type-tool',
  }),
];

function renderCatalog() {
  const { queryClient, spy } = createFeedbackTestQueryClientWithSpy();
  const view = render(
    <QueryClientProvider client={queryClient}>
      <TypeCatalogSettings />
    </QueryClientProvider>,
  );
  return { ...view, spy };
}

const switchFor = (plural: string) => screen.findByRole('switch', { name: `Activate ${plural}` });

describe('TypeCatalogSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResourceTypeCatalog).mockResolvedValue(catalog);
    vi.mocked(activateCatalogType).mockResolvedValue({ displayName: 'Drill' } as never);
    vi.mocked(deactivateCatalogType).mockResolvedValue(undefined);
    vi.mocked(purgeCatalogType).mockResolvedValue({
      resources: 4,
      assignments: 7,
      groups: 1,
      requestTargets: 2,
    });
  });

  it('groups entries by category and shows the preset-field count', async () => {
    renderCatalog();

    expect(await screen.findByText('Stationary equipment')).toBeInTheDocument();
    expect(screen.getByText('Mobile assets')).toBeInTheDocument();
    expect(screen.getByText('Drills')).toBeInTheDocument();
    expect(screen.getByText('People')).toBeInTheDocument();
    expect(screen.getAllByText('5 preset fields')).toHaveLength(2);
    // In-use entries show their resource count next to the field count.
    expect(screen.getByText('1 preset fields · 4 resources')).toBeInTheDocument();
  });

  it("shows the tenant's rename next to the catalog name", async () => {
    renderCatalog();

    expect(await screen.findByText('renamed to Mitarbeiter')).toBeInTheDocument();
  });

  it('reflects each state in its switch', async () => {
    renderCatalog();

    expect(await switchFor('Drills')).not.toBeChecked();
    expect(await switchFor('Lathes')).not.toBeChecked();
    expect(await switchFor('People')).toBeChecked();
  });

  it('switching on activates the entry', async () => {
    const user = userEvent.setup();
    renderCatalog();

    await user.click(await switchFor('Drills'));

    await waitFor(() => expect(activateCatalogType).toHaveBeenCalledWith('drill'));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Drill activated'));
  });

  it('switching off an unused entry hides it without asking', async () => {
    const user = userEvent.setup();
    renderCatalog();

    await user.click(await switchFor('Tools'));

    await waitFor(() => expect(deactivateCatalogType).toHaveBeenCalledWith('tool'));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('switching off an in-use entry asks first, and Hide keeps the data', async () => {
    const user = userEvent.setup();
    renderCatalog();

    await user.click(await switchFor('People'));

    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent('Deactivate Mitarbeiter?');
    expect(dialog).toHaveTextContent('4 resources');
    expect(dialog).toHaveTextContent('2 request targets');
    expect(deactivateCatalogType).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Hide type' }));

    await waitFor(() => expect(deactivateCatalogType).toHaveBeenCalledWith('person'));
    expect(purgeCatalogType).not.toHaveBeenCalled();
  });

  it('cancelling the in-use dialog changes nothing', async () => {
    const user = userEvent.setup();
    renderCatalog();

    await user.click(await switchFor('People'));
    await screen.findByRole('alertdialog');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
    expect(deactivateCatalogType).not.toHaveBeenCalled();
    expect(purgeCatalogType).not.toHaveBeenCalled();
  });

  it('the delete path needs a second, explicit confirmation', async () => {
    const user = userEvent.setup();
    const { spy } = renderCatalog();

    await user.click(await switchFor('People'));
    await screen.findByRole('alertdialog');
    await user.click(screen.getByRole('button', { name: 'Delete everything…' }));

    // Step 2 names what will be lost; nothing has been deleted yet.
    const confirm = await screen.findByRole('alertdialog');
    expect(confirm).toHaveTextContent('Delete "Mitarbeiter" and all its data?');
    expect(confirm).toHaveTextContent('4 resources');
    expect(confirm).toHaveTextContent('booking history');
    expect(purgeCatalogType).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Delete everything' }));

    await waitFor(() => expect(purgeCatalogType).toHaveBeenCalledWith('person'));
    await waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith(
        'Type deleted (4 resources, 7 assignments removed)',
      ),
    );
    // A purge refreshes everything that rendered the deleted data.
    expect(spy).toHaveBeenCalledWith({ queryKey: qk.resourceTypeCatalog.all(), exact: false });
    expect(spy).toHaveBeenCalledWith({ queryKey: qk.resourceTypes.all(), exact: false });
    expect(spy).toHaveBeenCalledWith({ queryKey: qk.resources.all(), exact: false });
    expect(spy).toHaveBeenCalledWith({ queryKey: qk.requests.all(), exact: false });
  });

  it('surfaces a load failure with a retry', async () => {
    vi.mocked(getResourceTypeCatalog).mockRejectedValue(new Error('boom'));
    renderCatalog();

    expect(await screen.findByText('boom')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});

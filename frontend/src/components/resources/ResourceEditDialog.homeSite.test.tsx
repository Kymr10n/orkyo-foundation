import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { ResourceEditDialog } from './ResourceEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
  updateResource: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
}));

const sites = [
  { id: 'site-1', name: 'Plant North' },
  { id: 'site-2', name: 'Plant South' },
];
const useSitesMock = vi.fn();
const useIsMultiSiteMock = vi.fn();
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => useSitesMock(),
  useIsMultiSite: () => useIsMultiSiteMock(),
}));

// The top-bar site. The list that opens the dialog is scoped by it.
vi.mock('@foundation/src/store/site-store', () => ({
  useSiteStore: (selector: (s: { selectedSiteId: string | null }) => unknown) =>
    selector({ selectedSiteId: 'site-1' }),
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { createTestQueryClient } from '@foundation/src/test-utils';

function renderDialog(resource: ResourceInfo | null = null) {
  const { queryClient } = createTestQueryClient({ feedback: true });
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceEditDialog
        resourceType={machineResourceType}
        resource={resource}
        open
        onOpenChange={() => {}}
      />
    </QueryClientProvider>,
  );
}

async function saveNew(name: string) {
  await userEvent.type(screen.getByLabelText('Name'), name);
  await userEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(createResource).toHaveBeenCalled());
  return vi.mocked(createResource).mock.calls[0][0];
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getResourceCustomFields).mockResolvedValue([]);
  vi.mocked(createResource).mockResolvedValue({ id: 'new' } as ResourceInfo);
  vi.mocked(updateResource).mockResolvedValue({ id: 'r-1' } as ResourceInfo);
});

describe('ResourceEditDialog home site', () => {
  it('creates under the selected site on a single-site tenant, where the picker is hidden', async () => {
    // The bug: with no picker on screen the site was never set, the row was saved with
    // homeSiteId null, and the site-scoped list that opened the dialog never showed it.
    useSitesMock.mockReturnValue({ data: sites.slice(0, 1) });
    useIsMultiSiteMock.mockReturnValue(false);
    renderDialog();

    expect(screen.queryByLabelText('Home Site')).not.toBeInTheDocument();
    expect(await saveNew('Mill 1')).toMatchObject({ homeSiteId: 'site-1' });
  });

  it('preselects the selected site on a multi-site tenant', async () => {
    useSitesMock.mockReturnValue({ data: sites });
    useIsMultiSiteMock.mockReturnValue(true);
    renderDialog();

    expect(screen.getByLabelText('Home Site')).toHaveTextContent('Plant North');
    expect(await saveNew('Mill 1')).toMatchObject({ homeSiteId: 'site-1' });
  });

  it('still lets a multi-site tenant create a resource with no site', async () => {
    useSitesMock.mockReturnValue({ data: sites });
    useIsMultiSiteMock.mockReturnValue(true);
    renderDialog();

    await userEvent.click(screen.getByLabelText('Home Site'));
    await userEvent.click(await screen.findByRole('option', { name: 'Unset' }));

    expect(await saveNew('Mill 1')).toMatchObject({ homeSiteId: null });
  });

  it('keeps the edited resource’s own site rather than the selected one', async () => {
    useSitesMock.mockReturnValue({ data: sites });
    useIsMultiSiteMock.mockReturnValue(true);
    renderDialog({
      id: 'r-1',
      name: 'Mill 1',
      resourceTypeKey: 'machine',
      homeSiteId: 'site-2',
    } as ResourceInfo);

    expect(screen.getByLabelText('Home Site')).toHaveTextContent('Plant South');
    await userEvent.type(screen.getByLabelText('Name'), ' A');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateResource).toHaveBeenCalled());
    expect(vi.mocked(updateResource).mock.calls[0][1]).toMatchObject({ homeSiteId: 'site-2' });
  });
});

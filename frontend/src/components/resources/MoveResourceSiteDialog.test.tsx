import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { MoveResourceSiteDialog } from './MoveResourceSiteDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  updateResource: vi.fn(),
}));

const sites = [
  { id: 'site-1', name: 'Plant North' },
  { id: 'site-2', name: 'Plant South' },
];
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: sites }),
}));

import { updateResource } from '@foundation/src/lib/api/resources-api';
import { createTestQueryClient } from '@foundation/src/test-utils';

const mill = {
  id: 'r-1',
  name: 'Mill 1',
  resourceTypeKey: 'machine',
  homeSiteId: 'site-1',
} as ResourceInfo;

function renderDialog(resource: ResourceInfo = mill) {
  const onOpenChange = vi.fn();
  const { queryClient } = createTestQueryClient({ feedback: true });
  render(
    <QueryClientProvider client={queryClient}>
      <MoveResourceSiteDialog resource={resource} open onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

const moveButton = () => screen.getByRole('button', { name: 'Move' });

async function pick(siteName: string) {
  await userEvent.click(screen.getByLabelText('Site'));
  await userEvent.click(await screen.findByRole('option', { name: siteName }));
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(updateResource).mockResolvedValue({ id: 'r-1' } as ResourceInfo);
});

describe('MoveResourceSiteDialog', () => {
  it('preselects the current home site and disables Move until it changes', () => {
    renderDialog();
    expect(screen.getByLabelText('Site')).toHaveTextContent('Plant North');
    expect(moveButton()).toBeDisabled();
  });

  it('sends only the new home site and closes', async () => {
    const { onOpenChange } = renderDialog();
    await pick('Plant South');
    expect(moveButton()).toBeEnabled();
    await userEvent.click(moveButton());

    await waitFor(() => expect(updateResource).toHaveBeenCalledWith('r-1', { homeSiteId: 'site-2' }));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it('shows the failure inline and stays open', async () => {
    vi.mocked(updateResource).mockRejectedValue(new Error("Code 'M-1' already exists for this site"));
    const { onOpenChange } = renderDialog();
    await pick('Plant South');
    await userEvent.click(moveButton());

    expect(await screen.findByText("Code 'M-1' already exists for this site")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it('starts empty for a resource without a home site and enables Move once one is picked', async () => {
    renderDialog({ ...mill, homeSiteId: null });
    expect(screen.getByLabelText('Site')).toHaveTextContent('Select a site');
    expect(moveButton()).toBeDisabled();

    await pick('Plant North');
    expect(moveButton()).toBeEnabled();
  });
});

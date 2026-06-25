import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ResourceGroupList } from './ResourceGroupList';
import type { ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';

vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  getResourceGroups: vi.fn(),
  deleteResourceGroup: vi.fn(),
  createResourceGroup: vi.fn(),
  updateResourceGroup: vi.fn(),
  getResourceGroupMembers: vi.fn().mockResolvedValue({ groupId: 'g-1', members: [] }),
  setResourceGroupMembers: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn().mockResolvedValue({ data: [], total: 0, page: 1, pageSize: 100 }),
}));

import { getResourceGroups, deleteResourceGroup } from '@foundation/src/lib/api/resource-groups-api';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { toast } from 'sonner';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const mockGroups: ResourceGroupInfo[] = [
  {
    id: 'g-1',
    name: 'Engineering',
    description: 'Engineering team',
    defaultAvailabilityPercent: 100,
    memberCount: 5,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    resourceTypeKey: 'person',
  },
  {
    id: 'g-2',
    name: 'Design',
    description: undefined,
    defaultAvailabilityPercent: 80,
    memberCount: 3,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    resourceTypeKey: 'person',
  },
];

function renderList(resourceTypeKey = 'person') {
  // Delete feedback flows through the meta-driven MutationCache (matching prod).
  const queryClient: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => queryClient, toast),
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceGroupList resourceTypeKey={resourceTypeKey} />
    </QueryClientProvider>,
  );
}

// Row actions live behind a labelled kebab menu (RowActions). The groups query
// resolves after first paint and re-renders the table, which would dismiss a
// freshly-opened menu — retry the open until an item sticks.
async function openRowMenu(user: ReturnType<typeof userEvent.setup>, name: string) {
  await waitFor(async () => {
    if (screen.queryAllByRole('menuitem').length === 0) {
      await user.click(screen.getByRole('button', { name: `Actions for ${name}` }));
    }
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThan(0);
  });
}

describe('ResourceGroupList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResourceGroups).mockResolvedValue(mockGroups);
    vi.mocked(deleteResourceGroup).mockResolvedValue(undefined);
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('disables all edit affordances for a viewer who cannot edit', async () => {
    const user = userEvent.setup();
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderList();
    await waitFor(() => expect(screen.getByText('Engineering')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Add Group/i })).toBeDisabled();
    await openRowMenu(user, 'Engineering');
    expect(screen.getByRole('menuitem', { name: /^Edit/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('menuitem', { name: /^Delete/ })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('menuitem', { name: /Manage members/ })).toHaveAttribute('aria-disabled', 'true');
  });

  it('renders Add Group button', async () => {
    renderList();
    expect(screen.getByRole('button', { name: /Add Group/i })).toBeInTheDocument();
  });

  it('shows loading state initially', () => {
    vi.mocked(getResourceGroups).mockImplementation(() => new Promise(() => {}));
    renderList();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders group rows after loading', async () => {
    renderList();
    await waitFor(() => expect(screen.getByText('Engineering')).toBeInTheDocument());
    expect(screen.getByText('Design')).toBeInTheDocument();
    expect(screen.getByText('Engineering team')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByText('80%')).toBeInTheDocument();
  });

  it('shows empty state when no groups', async () => {
    vi.mocked(getResourceGroups).mockResolvedValue([]);
    renderList();
    await waitFor(() => expect(screen.getByText(/No groups yet/i)).toBeInTheDocument());
  });

  it('fetches groups with provided resourceTypeKey', async () => {
    renderList('tool');
    await waitFor(() => expect(getResourceGroups).toHaveBeenCalledWith('tool'));
  });

  it('opens Add Group dialog when Add Group clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByRole('button', { name: /Add Group/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('opens Edit dialog when edit action clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await openRowMenu(user, 'Engineering');
    await user.click(screen.getByRole('menuitem', { name: /^Edit/ }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('opens the Edit dialog prefilled when a row is clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByText('Engineering'));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Engineering')).toBeInTheDocument();
  });

  it('does not trigger the row edit-click when an action is invoked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await openRowMenu(user, 'Engineering');
    await user.click(screen.getByRole('menuitem', { name: /^Delete/ }));
    // The action menu's stopPropagation means the row's edit-onClick never fired,
    // so the only dialog present is the delete confirmation (an alertdialog).
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('does not make rows clickable for a viewer who cannot edit', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByText('Engineering'));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('confirms before deleting, then calls deleteResourceGroup', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await openRowMenu(user, 'Engineering');
    await user.click(screen.getByRole('menuitem', { name: /^Delete/ }));
    // Delete is now guarded by the shared ConfirmDialog (closes the data-loss
    // footgun where deletion fired immediately with no confirmation).
    expect(deleteResourceGroup).not.toHaveBeenCalled();
    await user.click(await screen.findByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteResourceGroup).toHaveBeenCalledWith('g-1', expect.anything()));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Group deleted'));
  });

  it('opens the Manage Members editor when the action is clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));

    await openRowMenu(user, 'Engineering');
    await user.click(screen.getByRole('menuitem', { name: /Manage members/ }));

    await waitFor(() =>
      expect(screen.getByText(/Manage Members in/i)).toBeInTheDocument(),
    );
  });
});

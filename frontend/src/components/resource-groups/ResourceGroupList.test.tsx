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
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceGroupList resourceTypeKey={resourceTypeKey} />
    </QueryClientProvider>,
  );
}

describe('ResourceGroupList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResourceGroups).mockResolvedValue(mockGroups);
    vi.mocked(deleteResourceGroup).mockResolvedValue(undefined);
  });

  it('renders Add Group button', async () => {
    renderList();
    expect(screen.getByRole('button', { name: /Add Group/i })).toBeInTheDocument();
  });

  it('shows loading state initially', () => {
    vi.mocked(getResourceGroups).mockImplementation(() => new Promise(() => {}));
    renderList();
    expect(document.querySelector('.animate-spin')).toBeInTheDocument();
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

  it('opens Edit dialog when edit button clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByRole('button', { name: /Edit Engineering/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('calls deleteResourceGroup when delete button clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByRole('button', { name: /Delete Engineering/i }));
    await waitFor(() => expect(deleteResourceGroup).toHaveBeenCalledWith('g-1', expect.anything()));
  });

  it('opens the Manage Members editor when the Users icon is clicked', async () => {
    const user = userEvent.setup();
    renderList();
    await waitFor(() => screen.getByText('Engineering'));

    await user.click(
      screen.getByRole('button', { name: /Manage members of Engineering/i }),
    );

    await waitFor(() =>
      expect(screen.getByText(/Manage Members in/i)).toBeInTheDocument(),
    );
  });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ResourceGroupEditDialog } from './ResourceGroupEditDialog';
import type { ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';

vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  createResourceGroup: vi.fn(),
  updateResourceGroup: vi.fn(),
  getResourceGroups: vi.fn(),
  deleteResourceGroup: vi.fn(),
}));

import { createResourceGroup, updateResourceGroup } from '@foundation/src/lib/api/resource-groups-api';

const mockGroup: ResourceGroupInfo = {
  id: 'g-1',
  name: 'Hall A',
  description: 'Main hall',
  defaultAvailabilityPercent: 80,
  memberCount: 2,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  resourceTypeKey: 'space',
};

type DialogProps = React.ComponentProps<typeof ResourceGroupEditDialog>;

function renderDialog(props: Partial<DialogProps> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ResourceGroupEditDialog
        resourceTypeKey="space"
        group={null}
        isOpen={true}
        onClose={() => {}}
        onSaved={() => {}}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('ResourceGroupEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createResourceGroup).mockResolvedValue(mockGroup);
    vi.mocked(updateResourceGroup).mockResolvedValue(mockGroup);
  });

  it('renders "Add Group" title when group is null', () => {
    renderDialog();
    expect(screen.getByText('Add Group')).toBeInTheDocument();
  });

  it('renders "Edit Group" title when group is provided', () => {
    renderDialog({ group: mockGroup });
    expect(screen.getByText('Edit Group')).toBeInTheDocument();
  });

  it('populates fields from group when editing', () => {
    renderDialog({ group: mockGroup });
    expect(screen.getByDisplayValue('Hall A')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Main hall')).toBeInTheDocument();
    expect(screen.getByDisplayValue('80')).toBeInTheDocument();
  });

  it('Save button is disabled when name is empty', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
  });

  it('Save button is enabled when name is filled', async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.type(screen.getByLabelText(/Name \*/i), 'New Hall');
    expect(screen.getByRole('button', { name: /Save/i })).not.toBeDisabled();
  });

  it('calls createResourceGroup with resourceTypeKey prop on create', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    renderDialog({ resourceTypeKey: 'tool', onSaved });
    await user.type(screen.getByLabelText(/Name \*/i), 'Power Tools');
    await user.click(screen.getByRole('button', { name: /Save/i }));
    await waitFor(() =>
      expect(createResourceGroup).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: 'tool', name: 'Power Tools' }),
      ),
    );
    expect(onSaved).toHaveBeenCalled();
  });

  it('calls updateResourceGroup with correct id on edit', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    renderDialog({ group: mockGroup, onSaved });
    const nameInput = screen.getByDisplayValue('Hall A');
    await user.clear(nameInput);
    await user.type(nameInput, 'Hall B');
    await user.click(screen.getByRole('button', { name: /Save/i }));
    await waitFor(() =>
      expect(updateResourceGroup).toHaveBeenCalledWith(
        'g-1',
        expect.objectContaining({ name: 'Hall B' }),
      ),
    );
    expect(onSaved).toHaveBeenCalled();
  });

  it('calls onClose when Cancel is clicked', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    renderDialog({ onClose });
    await user.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(onClose).toHaveBeenCalled();
  });
});

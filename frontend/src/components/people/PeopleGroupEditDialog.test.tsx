import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PeopleGroupEditDialog } from './PeopleGroupEditDialog';
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
  name: 'Engineering',
  description: 'Engineering team',
  defaultAvailabilityPercent: 80,
  memberCount: 5,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderDialog(props: Partial<React.ComponentProps<typeof PeopleGroupEditDialog>> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PeopleGroupEditDialog
        group={null}
        isOpen={true}
        onClose={() => {}}
        onSaved={() => {}}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('PeopleGroupEditDialog', () => {
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
    expect(screen.getByDisplayValue('Engineering')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Engineering team')).toBeInTheDocument();
    expect(screen.getByDisplayValue('80')).toBeInTheDocument();
  });

  it('Save button is disabled when name is empty', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
  });

  it('Save button is enabled when name is filled', async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.type(screen.getByLabelText(/Name \*/i), 'New Group');
    expect(screen.getByRole('button', { name: /Save/i })).not.toBeDisabled();
  });

  it('calls createResourceGroup with correct payload on create', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    renderDialog({ onSaved });
    await user.type(screen.getByLabelText(/Name \*/i), 'New Group');
    await user.click(screen.getByRole('button', { name: /Save/i }));
    await waitFor(() =>
      expect(createResourceGroup).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: 'person', name: 'New Group' }),
      ),
    );
    expect(onSaved).toHaveBeenCalled();
  });

  it('calls updateResourceGroup with correct id on edit', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    renderDialog({ group: mockGroup, onSaved });
    const nameInput = screen.getByDisplayValue('Engineering');
    await user.clear(nameInput);
    await user.type(nameInput, 'Engineering Updated');
    await user.click(screen.getByRole('button', { name: /Save/i }));
    await waitFor(() =>
      expect(updateResourceGroup).toHaveBeenCalledWith(
        'g-1',
        expect.objectContaining({ name: 'Engineering Updated' }),
      ),
    );
    expect(onSaved).toHaveBeenCalled();
  });

  it('calls updateResourceGroup with defaultAvailabilityPercent', async () => {
    const user = userEvent.setup();
    renderDialog({ group: mockGroup });
    const availInput = screen.getByDisplayValue('80');
    await user.clear(availInput);
    await user.type(availInput, '90');
    await user.click(screen.getByRole('button', { name: /Save/i }));
    await waitFor(() =>
      expect(updateResourceGroup).toHaveBeenCalledWith(
        'g-1',
        expect.objectContaining({ defaultAvailabilityPercent: 90 }),
      ),
    );
  });

  it('Cancel button calls onClose', () => {
    const onClose = vi.fn();
    renderDialog({ onClose });
    screen.getByRole('button', { name: /Cancel/i }).click();
    expect(onClose).toHaveBeenCalled();
  });
});

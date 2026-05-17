/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { GroupSettings } from './GroupSettings';

const {
  mockCreateResourceGroup,
  mockUpdateResourceGroup,
  mockDeleteResourceGroup,
  mockGetResourceGroups,
} = vi.hoisted(() => ({
  mockCreateResourceGroup: vi.fn(),
  mockUpdateResourceGroup: vi.fn(),
  mockDeleteResourceGroup: vi.fn(),
  mockGetResourceGroups: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  getResourceGroups: mockGetResourceGroups,
  createResourceGroup: mockCreateResourceGroup,
  updateResourceGroup: mockUpdateResourceGroup,
  deleteResourceGroup: mockDeleteResourceGroup,
}));

let capturedSpacesEditorProps: any = null;
let capturedCapabilitiesEditorProps: any = null;

vi.mock('./GroupCapabilitiesEditor', () => ({
  GroupCapabilitiesEditor: (props: any) => {
    capturedCapabilitiesEditorProps = props;
    return props.open ? <div data-testid="capabilities-editor">{props.groupName}</div> : null;
  },
}));

vi.mock('./GroupSpacesEditor', () => ({
  GroupSpacesEditor: (props: any) => {
    capturedSpacesEditorProps = props;
    return props.open ? <div data-testid="spaces-editor">{props.groupName}</div> : null;
  },
}));

const mockGroups = [
  { id: 'g1', name: 'Meeting Rooms', description: 'All meeting rooms', color: '#3b82f6', displayOrder: 0, memberCount: 3, defaultAvailabilityPercent: 100, createdAt: '', updatedAt: '', resourceTypeKey: 'space' },
  { id: 'g2', name: 'Classrooms', description: '', color: '#ef4444', displayOrder: 1, memberCount: 0, defaultAvailabilityPercent: 100, createdAt: '', updatedAt: '', resourceTypeKey: 'space' },
];

function renderGroupSettings(editGroupId?: string | null) {
  return render(
      <MemoryRouter>
      <GroupSettings editGroupId={editGroupId} />
    </MemoryRouter>,
  );
}

describe('GroupSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetResourceGroups.mockResolvedValue([]);
    mockCreateResourceGroup.mockResolvedValue({ id: 'new-group' });
    mockUpdateResourceGroup.mockResolvedValue({ id: 'g1' });
    mockDeleteResourceGroup.mockResolvedValue(undefined);
    capturedSpacesEditorProps = null;
    capturedCapabilitiesEditorProps = null;
    global.confirm = vi.fn(() => true);
    global.alert = vi.fn();
  });

  it('renders header and create button', async () => {
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Space Groups')).toBeInTheDocument();
    });
    expect(screen.getByText('New Group')).toBeInTheDocument();
  });

  it('shows loading state initially', async () => {
    renderGroupSettings();
    expect(screen.getByText('Loading space groups...')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByText('Loading space groups...')).not.toBeInTheDocument();
    });
  });

  it('shows empty state when no groups', async () => {
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('No space groups yet')).toBeInTheDocument();
    });
    expect(screen.getByText('Create your first group')).toBeInTheDocument();
  });

  it('renders group table when groups exist', async () => {
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });
    expect(screen.getByText('Classrooms')).toBeInTheDocument();
    expect(screen.getByText('All meeting rooms')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument(); // memberCount
  });

  it('shows error when loading fails', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    mockGetResourceGroups.mockRejectedValue(new Error('Network error'));
    renderGroupSettings();
    // The component logs error and sets error state — keep loading false after catch
    await waitFor(() => {
      expect(screen.queryByText('Loading space groups...')).not.toBeInTheDocument();
    });
    consoleError.mockRestore();
  });

  it('opens create dialog on New Group click', async () => {
    const user = userEvent.setup();
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('New Group')).toBeInTheDocument();
    });
    await user.click(screen.getByText('New Group'));
    await waitFor(() => {
      expect(screen.getByText('Create New Group')).toBeInTheDocument();
    });
  });

  it('opens create dialog from empty-state button', async () => {
    const user = userEvent.setup();
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Create your first group')).toBeInTheDocument();
    });
    await user.click(screen.getByText('Create your first group'));
    await waitFor(() => {
      expect(screen.getByText('Create New Group')).toBeInTheDocument();
    });
  });

  it('validates empty name on save', async () => {
    const user = userEvent.setup();
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('New Group')).toBeInTheDocument();
    });
    await user.click(screen.getByText('New Group'));
    await waitFor(() => {
      expect(screen.getByText('Create New Group')).toBeInTheDocument();
    });
    // Name starts empty, click Create
    await user.click(screen.getByRole('button', { name: 'Create' }));
    await waitFor(() => {
      expect(screen.getByText('Name is required')).toBeInTheDocument();
    });
    expect(mockCreateResourceGroup).not.toHaveBeenCalled();
  });

  it('creates a group successfully', async () => {
    const user = userEvent.setup();
    // After creation, groups will be reloaded
    mockGetResourceGroups.mockResolvedValueOnce([]).mockResolvedValueOnce(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('New Group')).toBeInTheDocument();
    });
    await user.click(screen.getByText('New Group'));

    await waitFor(() => {
      expect(screen.getByLabelText('Name *')).toBeInTheDocument();
    });

    await user.type(screen.getByLabelText('Name *'), 'Meeting Rooms');
    await user.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => {
      expect(mockCreateResourceGroup).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Meeting Rooms' }),
      );
    });
  });

  it('opens edit dialog when edit button is clicked', async () => {
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    const editBtn = screen.getAllByTitle('Edit Group')[0];
    await user.click(editBtn);

    await waitFor(() => {
      expect(screen.getByText('Edit Group')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Meeting Rooms')).toBeInTheDocument();
    });
  });

  it('updates a group on save in edit mode', async () => {
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Edit Group')[0]);
    await waitFor(() => {
      expect(screen.getByDisplayValue('Meeting Rooms')).toBeInTheDocument();
    });

    const nameInput = screen.getByDisplayValue('Meeting Rooms');
    await user.clear(nameInput);
    await user.type(nameInput, 'Updated Rooms');
    await user.click(screen.getByRole('button', { name: 'Update' }));

    await waitFor(() => {
      expect(mockUpdateResourceGroup).toHaveBeenCalledWith(
        'g1',
        expect.objectContaining({ name: 'Updated Rooms' }),
      );
    });
  });

  it('deletes a group with confirmation', async () => {
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Delete Group')[0]);

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalledWith(expect.stringContaining('Meeting Rooms'));
      expect(mockDeleteResourceGroup).toHaveBeenCalledWith('g1');
    });
  });

  it('does not delete when confirmation is declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Delete Group')[0]);

    expect(global.confirm).toHaveBeenCalled();
    expect(mockDeleteResourceGroup).not.toHaveBeenCalled();
  });

  it('shows alert on delete error', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    mockDeleteResourceGroup.mockRejectedValueOnce(new Error('Delete failed'));
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Delete Group')[0]);

    await waitFor(() => {
      expect(global.alert).toHaveBeenCalledWith('Delete failed');
    });
    consoleError.mockRestore();
  });

  it('shows save error in dialog', async () => {
    mockCreateResourceGroup.mockRejectedValueOnce(new Error('Save error'));
    const user = userEvent.setup();
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('New Group')).toBeInTheDocument();
    });
    await user.click(screen.getByText('New Group'));
    await waitFor(() => {
      expect(screen.getByLabelText('Name *')).toBeInTheDocument();
    });
    await user.type(screen.getByLabelText('Name *'), 'Test');
    await user.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => {
      expect(screen.getByText('Save error')).toBeInTheDocument();
    });
  });

  it('opens spaces editor for a group', async () => {
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Manage Spaces')[0]);

    await waitFor(() => {
      expect(screen.getByTestId('spaces-editor')).toBeInTheDocument();
      expect(capturedSpacesEditorProps.groupId).toBe('g1');
    });
  });

  it('opens capabilities editor for a group', async () => {
    const user = userEvent.setup();
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });

    await user.click(screen.getAllByTitle('Manage Capabilities')[0]);

    await waitFor(() => {
      expect(screen.getByTestId('capabilities-editor')).toBeInTheDocument();
      expect(capturedCapabilitiesEditorProps.groupId).toBe('g1');
    });
  });

  it('displays color swatch for groups', async () => {
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    const { container } = renderGroupSettings();
    await waitFor(() => {
      expect(screen.getByText('Meeting Rooms')).toBeInTheDocument();
    });
    const swatches = container.querySelectorAll('[style*="background-color"]');
    expect(swatches.length).toBeGreaterThanOrEqual(2);
  });

  it('opens edit dialog via editGroupId prop', async () => {
    mockGetResourceGroups.mockResolvedValue(mockGroups);
    renderGroupSettings('g1');
    await waitFor(() => {
      expect(screen.getByText('Edit Group')).toBeInTheDocument();
      expect(screen.getByDisplayValue('Meeting Rooms')).toBeInTheDocument();
    });
  });
});

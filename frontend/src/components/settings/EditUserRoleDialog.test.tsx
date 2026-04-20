import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EditUserRoleDialog } from './EditUserRoleDialog';
import * as userApi from '@/lib/api/user-api';

vi.mock('@/lib/api/user-api');

describe('EditUserRoleDialog', () => {
  let queryClient: QueryClient;
  const mockOnSuccess = vi.fn();
  const mockOnOpenChange = vi.fn();

  const mockUser: userApi.UserWithRole = {
    id: '1',
    email: 'test@example.com',
    displayName: 'Test User',
    role: 'viewer',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
    lastLoginAt: '2024-01-10T00:00:00Z',
  };

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    vi.clearAllMocks();
    vi.mocked(userApi.updateUserRole).mockResolvedValue({
      ...mockUser,
      role: 'editor',
    });
  });

  it('renders nothing when closed', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={false}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );
    expect(screen.queryByText('Edit User Role')).not.toBeInTheDocument();
  });

  it('renders dialog content when open', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    expect(screen.getByText('Edit User Role')).toBeInTheDocument();
    expect(screen.getByText(/Test User/)).toBeInTheDocument();
    expect(screen.getByText(/test@example.com/)).toBeInTheDocument();
    expect(screen.getByLabelText('New Role')).toBeInTheDocument();
  });

  it('shows current role', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    expect(screen.getByText(/Current role:/)).toBeInTheDocument();
    expect(screen.getByText('viewer')).toBeInTheDocument();
  });

  it('initializes role select with current user role', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Default description for viewer role should be visible
    expect(screen.getByText(/Viewers have read-only access/)).toBeInTheDocument();
  });

  it('allows changing role', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);

    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    await waitFor(() => {
      expect(screen.getByText(/Editors can create and modify/)).toBeInTheDocument();
    });
  });

  it('submits role change successfully', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    // Submit
    const saveButton = screen.getByRole('button', { name: /Save Changes/i });
    await user.click(saveButton);

    await waitFor(() => {
      expect(userApi.updateUserRole).toHaveBeenCalledWith('1', { role: 'editor' });
      expect(mockOnSuccess).toHaveBeenCalled();
    });
  });

  it('prevents submission if role not changed', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const saveButton = screen.getByRole('button', { name: /Save Changes/i });
    await user.click(saveButton);

    await waitFor(() => {
      expect(screen.getByText('No changes to save')).toBeInTheDocument();
    });

    expect(userApi.updateUserRole).not.toHaveBeenCalled();
  });

  it('shows warning when changing to admin', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const adminOption = await screen.findByRole('option', { name: /Admin/i });
    await user.click(adminOption);

    await waitFor(() => {
      expect(
        screen.getByText(/This will grant full administrative access/)
      ).toBeInTheDocument();
    });
  });

  it('shows warning when changing to inactive', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const inactiveOption = await screen.findByRole('option', { name: /Inactive/i });
    await user.click(inactiveOption);

    await waitFor(() => {
      expect(
        screen.getByText(/This will immediately revoke all access/)
      ).toBeInTheDocument();
    });
  });

  it('does not show warning for editor role', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    await waitFor(() => {
      expect(screen.getByText(/Editors can create and modify/)).toBeInTheDocument();
    });

    expect(screen.queryByText(/This will grant/)).not.toBeInTheDocument();
    expect(screen.queryByText(/This will immediately revoke/)).not.toBeInTheDocument();
  });

  it('displays API error messages', async () => {
    vi.mocked(userApi.updateUserRole).mockRejectedValue(
      new Error('Permission denied')
    );

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    // Submit
    const saveButton = screen.getByRole('button', { name: /Save Changes/i });
    await user.click(saveButton);

    await waitFor(() => {
      expect(screen.getByText('Permission denied')).toBeInTheDocument();
    });

    expect(mockOnSuccess).not.toHaveBeenCalled();
  });

  it('disables form during submission', async () => {
    vi.mocked(userApi.updateUserRole).mockImplementation(
      () => new Promise(() => {}) // Never resolves
    );

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    // Submit
    const saveButton = screen.getByRole('button', { name: /Save Changes/i });
    await user.click(saveButton);

    await waitFor(() => {
      expect(screen.getByText('Saving...')).toBeInTheDocument();
      expect(saveButton).toBeDisabled();
    });
  });

  it('closes dialog on cancel', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const cancelButton = screen.getByRole('button', { name: /Cancel/i });
    await user.click(cancelButton);

    expect(mockOnOpenChange).toHaveBeenCalledWith(false);
  });

  it('resets role to original on cancel', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const adminOption = await screen.findByRole('option', { name: /Admin/i });
    await user.click(adminOption);

    // Cancel
    const cancelButton = screen.getByRole('button', { name: /Cancel/i });
    await user.click(cancelButton);

    expect(mockOnOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows all role options including inactive', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <EditUserRoleDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          user={mockUser}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Viewer/i })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: /Editor/i })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: /Admin/i })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: /Inactive/i })).toBeInTheDocument();
    });
  });
});

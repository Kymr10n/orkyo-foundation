/* eslint-disable @typescript-eslint/no-explicit-any */
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { UserSettings } from './UserSettings';
import * as userApi from '@foundation/src/lib/api/user-api';

vi.mock('@foundation/src/lib/api/user-api');
vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(),
  useImportHandler: vi.fn(),
}));

describe('UserSettings', () => {
  let queryClient: QueryClient;
  const mockUsers: userApi.UserWithRole[] = [
    {
      id: '1',
      email: 'admin@example.com',
      displayName: 'Admin User',
      role: 'admin',
      status: 'active',
      createdAt: '2024-01-01T00:00:00Z',
      lastLoginAt: '2024-01-10T00:00:00Z',
    },
    {
      id: '2',
      email: 'viewer@example.com',
      displayName: 'Viewer User',
      role: 'viewer',
      status: 'active',
      createdAt: '2024-01-02T00:00:00Z',
      lastLoginAt: undefined,
    },
  ];

  const mockInvitations: userApi.Invitation[] = [
    {
      id: 'inv-1',
      email: 'pending@example.com',
      role: 'editor',
      invitedBy: 'user-1',
      tokenHash: 'hash-123',
      createdAt: '2024-01-15T00:00:00Z',
      expiresAt: '2024-01-22T00:00:00Z',
    },
  ];

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    vi.clearAllMocks();
    vi.mocked(userApi.getUsers).mockResolvedValue(mockUsers);
    vi.mocked(userApi.getInvitations).mockResolvedValue(mockInvitations);
    vi.mocked(userApi.cancelInvitation).mockResolvedValue(undefined);
    vi.mocked(userApi.resendInvitation).mockResolvedValue(undefined);
    vi.mocked(userApi.deleteUser).mockResolvedValue(undefined);
    vi.mocked(userApi.updateUserRole).mockResolvedValue({
      ...mockUsers[0],
      role: 'editor',
    });

    // Mock window.confirm and window.alert
    global.confirm = vi.fn(() => true);
    global.alert = vi.fn();
  });

  it('renders loading state initially', () => {
    vi.mocked(userApi.getUsers).mockReturnValue(
      new Promise(() => {}) as any
    );
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );
    expect(screen.getByText('Loading users...')).toBeInTheDocument();
  });

  it('displays user list after loading', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Admin User')).toBeInTheDocument();
      expect(screen.getByText('admin@example.com')).toBeInTheDocument();
      expect(screen.getByText('Viewer User')).toBeInTheDocument();
      expect(screen.getByText('viewer@example.com')).toBeInTheDocument();
    });
  });

  it('displays pending invitations section', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/Pending Invitations/)).toBeInTheDocument();
      expect(screen.getByText('pending@example.com')).toBeInTheDocument();
    });
  });

  it('shows invite user button', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      const inviteButtons = screen.getAllByText('Invite User');
      expect(inviteButtons.length).toBeGreaterThan(0);
    });
  });

  it('displays correct role badges', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      const adminBadge = screen.getByText('admin');
      const viewerBadge = screen.getByText('viewer');
      const editorBadge = screen.getByText('editor');
      
      expect(adminBadge).toBeInTheDocument();
      expect(viewerBadge).toBeInTheDocument();
      expect(editorBadge).toBeInTheDocument();
    });
  });

  it('displays user status badges', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      const statusBadges = screen.getAllByText('active');
      expect(statusBadges.length).toBeGreaterThanOrEqual(2);
    });
  });

  it('handles cancel invitation', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('pending@example.com')).toBeInTheDocument();
    });

    // Find cancel button (X icon button)
    const cancelButtons = screen.getAllByRole('button', { name: /Cancel invitation/i });
    expect(cancelButtons.length).toBeGreaterThan(0);

    await user.click(cancelButtons[0]);

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalled();
      expect(userApi.cancelInvitation).toHaveBeenCalled();
      const [[invitationId]] = vi.mocked(userApi.cancelInvitation).mock.calls;
      expect(invitationId).toBe('inv-1');
    });
  });

  it('handles resend invitation', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('pending@example.com')).toBeInTheDocument();
    });

    // Find resend button
    const resendButtons = screen.getAllByRole('button', { name: /Resend invitation/i });
    expect(resendButtons.length).toBeGreaterThan(0);

    await user.click(resendButtons[0]);

    await waitFor(() => {
      expect(userApi.resendInvitation).toHaveBeenCalled();
      const [[invitationId]] = vi.mocked(userApi.resendInvitation).mock.calls;
      expect(invitationId).toBe('inv-1');
      expect(global.alert).toHaveBeenCalledWith('Invitation email resent successfully');
    });
  });

  it('handles delete user with confirmation', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Admin User')).toBeInTheDocument();
    });

    // Find remove button
    const removeButtons = screen.getAllByRole('button', { name: /Remove user/i });
    expect(removeButtons.length).toBeGreaterThan(0);

    await user.click(removeButtons[0]);

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalled();
      expect(userApi.deleteUser).toHaveBeenCalled();
      const [[userId]] = vi.mocked(userApi.deleteUser).mock.calls;
      expect(userId).toBe('1');
    });
  });

  it('does not delete user if confirmation declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();
    
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Admin User')).toBeInTheDocument();
    });

    const removeButtons = screen.getAllByRole('button', { name: /Remove user/i });
    await user.click(removeButtons[0]);

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalled();
    });
    
    expect(userApi.deleteUser).not.toHaveBeenCalled();
  });

  it('shows empty state when no users', async () => {
    vi.mocked(userApi.getUsers).mockResolvedValue([]);
    vi.mocked(userApi.getInvitations).mockResolvedValue([]);

    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('No users yet')).toBeInTheDocument();
      expect(screen.getByText('Invite your first user')).toBeInTheDocument();
    });
  });

  it('displays error state on API failure', async () => {
    vi.mocked(userApi.getUsers).mockRejectedValue(new Error('API Error'));

    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/API Error|Failed to load users/)).toBeInTheDocument();
      expect(screen.getByText('Retry')).toBeInTheDocument();
    });
  });

  it('shows last login date when available', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/Last login:/)).toBeInTheDocument();
    });
  });

  it('displays creation dates', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      const createdTexts = screen.getAllByText(/Created:/);
      expect(createdTexts.length).toBeGreaterThanOrEqual(2);
    });
  });

  it('displays invitation expiry dates', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <UserSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/Expires:/)).toBeInTheDocument();
      expect(screen.getByText(/Sent:/)).toBeInTheDocument();
    });
  });
});

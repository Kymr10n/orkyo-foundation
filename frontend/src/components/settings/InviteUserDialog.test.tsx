import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { InviteUserDialog } from './InviteUserDialog';
import * as userApi from '@/lib/api/user-api';

vi.mock('@/lib/api/user-api');

describe('InviteUserDialog', () => {
  let queryClient: QueryClient;
  const mockOnSuccess = vi.fn();
  const mockOnOpenChange = vi.fn();

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    vi.clearAllMocks();
    vi.mocked(userApi.createInvitation).mockResolvedValue({
      id: 'inv-1',
      email: 'test@example.com',
      role: 'viewer',
      invitedBy: 'user-1',
      tokenHash: 'hash-123',
      createdAt: new Date().toISOString(),
      expiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
    });
  });

  it('renders nothing when closed', () => {
    const { container: _container } = render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={false}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );
    expect(screen.queryByText('Invite User')).not.toBeInTheDocument();
  });

  it('renders dialog content when open', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    expect(screen.getByText('Invite User')).toBeInTheDocument();
    expect(screen.getByLabelText('Email Address')).toBeInTheDocument();
    expect(screen.getByLabelText('Role')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Send Invitation/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cancel/i })).toBeInTheDocument();
  });

  it('shows description text', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    expect(screen.getByText(/Send an invitation email/)).toBeInTheDocument();
  });

  it('has email field with placeholder', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByPlaceholderText('colleague@example.com');
    expect(emailInput).toBeInTheDocument();
    expect(emailInput).toHaveAttribute('type', 'email');
  });

  it('defaults role to viewer', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    expect(screen.getByText(/Viewers have read-only access/)).toBeInTheDocument();
  });

  it('validates required email field', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(screen.getByText('Email is required')).toBeInTheDocument();
    });

    expect(userApi.createInvitation).not.toHaveBeenCalled();
  });

  it('validates email format', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address');
    await user.clear(emailInput);
    await user.type(emailInput, 'invalid-email');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    // Give time for validation
    await new Promise(resolve => setTimeout(resolve, 100));

    // Email without @ should not be submitted to API
    expect(userApi.createInvitation).not.toHaveBeenCalled();
  });

  it('submits form with valid data', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address');
    await user.type(emailInput, 'newuser@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(userApi.createInvitation).toHaveBeenCalledWith({
        email: 'newuser@example.com',
        role: 'viewer',
      });
      expect(mockOnSuccess).toHaveBeenCalled();
    });
  });

  it('changes role selection', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Open role dropdown
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);

    // Select editor role
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    // Verify description changed
    await waitFor(() => {
      expect(screen.getByText(/Editors can create and modify/)).toBeInTheDocument();
    });
  });

  it('submits with editor role', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role to editor
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);

    // Enter email and submit
    const emailInput = screen.getByLabelText('Email Address');
    await user.type(emailInput, 'editor@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(userApi.createInvitation).toHaveBeenCalledWith({
        email: 'editor@example.com',
        role: 'editor',
      });
    });
  });

  it('submits with admin role', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Change role to admin
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const adminOption = await screen.findByRole('option', { name: /Admin/i });
    await user.click(adminOption);

    // Enter email and submit
    const emailInput = screen.getByLabelText('Email Address');
    await user.type(emailInput, 'admin@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(userApi.createInvitation).toHaveBeenCalledWith({
        email: 'admin@example.com',
        role: 'admin',
      });
    });
  });

  it('displays API error messages', async () => {
    vi.mocked(userApi.createInvitation).mockRejectedValue(
      new Error('User already invited')
    );

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address');
    await user.type(emailInput, 'existing@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(screen.getByText('User already invited')).toBeInTheDocument();
    });

    expect(mockOnSuccess).not.toHaveBeenCalled();
  });

  it('disables form during submission', async () => {
    vi.mocked(userApi.createInvitation).mockImplementation(
      () => new Promise(() => {}) // Never resolves
    );

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address') as HTMLInputElement;
    await user.type(emailInput, 'test@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(screen.getByText('Sending...')).toBeInTheDocument();
      expect(emailInput).toBeDisabled();
      expect(submitButton).toBeDisabled();
    });
  });

  it('clears form after successful submission', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address') as HTMLInputElement;
    await user.type(emailInput, 'test@example.com');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(mockOnSuccess).toHaveBeenCalled();
    });

    // Field should be cleared
    expect(emailInput.value).toBe('');
  });

  it('closes dialog on cancel', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const cancelButton = screen.getByRole('button', { name: /Cancel/i });
    await user.click(cancelButton);

    expect(mockOnOpenChange).toHaveBeenCalledWith(false);
  });

  it('trims email whitespace', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    const emailInput = screen.getByLabelText('Email Address');
    await user.type(emailInput, '  spaced@example.com  ');

    const submitButton = screen.getByRole('button', { name: /Send Invitation/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(userApi.createInvitation).toHaveBeenCalledWith({
        email: 'spaced@example.com',
        role: 'viewer',
      });
    });
  });

  it('shows role descriptions for all roles', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <InviteUserDialog
          open={true}
          onOpenChange={mockOnOpenChange}
          onSuccess={mockOnSuccess}
        />
      </QueryClientProvider>
    );

    // Check viewer description (default)
    expect(screen.getByText(/Viewers have read-only access/)).toBeInTheDocument();

    // Change to editor
    const roleSelect = screen.getByRole('combobox');
    await user.click(roleSelect);
    const editorOption = await screen.findByRole('option', { name: /Editor/i });
    await user.click(editorOption);
    
    await waitFor(() => {
      expect(screen.getByText(/Editors can create and modify/)).toBeInTheDocument();
    });

    // Change to admin
    await user.click(roleSelect);
    const adminOption = await screen.findByRole('option', { name: /Admin/i });
    await user.click(adminOption);

    await waitFor(() => {
      expect(screen.getByText(/Admins have full access/)).toBeInTheDocument();
    });
  });
});

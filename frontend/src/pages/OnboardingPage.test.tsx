import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

// Mock tenants-api
const mockCanCreateTenant = vi.fn();
const mockCreateTenant = vi.fn();
const mockGetStarterTemplates = vi.fn();
const mockGetTenantMemberships = vi.fn();
const mockCancelTenantDeletion = vi.fn();

vi.mock('@foundation/src/lib/api/tenant-account-api', () => ({
  canCreateTenant: () => mockCanCreateTenant(),
  createTenant: (...args: unknown[]) => mockCreateTenant(...args),
  getStarterTemplates: () => mockGetStarterTemplates(),
  getTenantMemberships: () => mockGetTenantMemberships(),
  cancelTenantDeletion: (...args: unknown[]) => mockCancelTenantDeletion(...args),
}));

import { OnboardingPage } from '@foundation/src/pages/OnboardingPage';

const MOCK_TEMPLATES = [
  { key: 'empty', name: 'Empty', description: 'Start from scratch', icon: 'file-plus', includesDemoData: false },
  { key: 'demo', name: 'Demo', description: 'Full sample data', icon: 'layout-dashboard', includesDemoData: true },
  { key: 'camping-site', name: 'Camping Site', description: 'Camping preset', icon: 'tent', includesDemoData: false },
];

const defaultProps = {
  onComplete: vi.fn().mockResolvedValue(undefined),
  onCancel: vi.fn(),
};

describe('OnboardingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCanCreateTenant.mockResolvedValue({ canCreate: true });
    mockGetStarterTemplates.mockResolvedValue(MOCK_TEMPLATES);
    mockGetTenantMemberships.mockResolvedValue([]);
    mockCancelTenantDeletion.mockResolvedValue(undefined);
    mockCreateTenant.mockResolvedValue({
      id: 'tenant-1',
      slug: 'my-company',
      displayName: 'My Company',
      state: 'active',
    });
    defaultProps.onComplete.mockResolvedValue(undefined);
  });

  it('renders welcome heading', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText('Welcome to Orkyo')).toBeInTheDocument();
    });
  });

  it('shows create button when user can create', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /create new organization/i })).toBeInTheDocument();
    });
  });

  it('shows form after clicking create button', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /create new organization/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /create new organization/i }));

    await waitFor(() => {
      expect(screen.getByLabelText(/organization name/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/url identifier/i)).toBeInTheDocument();
    });
  });

  it('auto-generates slug from display name', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      fireEvent.click(screen.getByRole('button', { name: /create new organization/i }));
    });

    await waitFor(() => {
      expect(screen.getByLabelText(/organization name/i)).toBeInTheDocument();
    });

    const nameInput = screen.getByLabelText(/organization name/i);
    fireEvent.change(nameInput, { target: { value: 'My Cool Company' } });

    const slugInput = screen.getByLabelText(/url identifier/i);
    expect((slugInput as HTMLInputElement).value).toBe('my-cool-company');
  });

  it('submits form and calls onComplete after creation', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      fireEvent.click(screen.getByRole('button', { name: /create new organization/i }));
    });

    await waitFor(() => {
      expect(screen.getByLabelText(/organization name/i)).toBeInTheDocument();
    });

    // Step 1: fill in org name
    fireEvent.change(screen.getByLabelText(/organization name/i), {
      target: { value: 'My Company' },
    });

    // Click Next to go to template step
    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    // Step 2: template picker should appear
    await waitFor(() => {
      expect(screen.getByText(/choose a starting point/i)).toBeInTheDocument();
    });

    // Click Create (default template is "empty")
    fireEvent.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockCreateTenant).toHaveBeenCalledWith({
        slug: 'my-company',
        displayName: 'My Company',
        starterTemplate: 'empty',
      });
    });

    await waitFor(() => {
      expect(defaultProps.onComplete).toHaveBeenCalled();
    });
  });

  it('allows selecting a different starter template', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      fireEvent.click(screen.getByRole('button', { name: /create new organization/i }));
    });

    await waitFor(() => {
      expect(screen.getByLabelText(/organization name/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/organization name/i), {
      target: { value: 'My Company' },
    });
    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    // Step 2: select Demo template
    await waitFor(() => {
      expect(screen.getByText('Demo')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Demo'));
    fireEvent.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockCreateTenant).toHaveBeenCalledWith(
        expect.objectContaining({ starterTemplate: 'demo' })
      );
    });
  });

  it('shows error when creation fails', async () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    mockCreateTenant.mockRejectedValue(new Error('Slug already taken'));

    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      fireEvent.click(screen.getByRole('button', { name: /create new organization/i }));
    });

    await waitFor(() => {
      expect(screen.getByLabelText(/organization name/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/organization name/i), {
      target: { value: 'My Company' },
    });

    // Go to step 2
    fireEvent.click(screen.getByRole('button', { name: /next/i }));

    await waitFor(() => {
      expect(screen.getByText(/choose a starting point/i)).toBeInTheDocument();
    });

    // Click Create
    fireEvent.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(screen.getByText(/slug already taken/i)).toBeInTheDocument();
    });
    consoleSpy.mockRestore();
  });

  it('shows sign out button', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument();
    });
  });

  it('handles sign out via onCancel', async () => {
    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /sign out/i }));

    expect(defaultProps.onCancel).toHaveBeenCalled();
  });

  it('shows message when user cannot create', async () => {
    mockCanCreateTenant.mockResolvedValue({ canCreate: false });

    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/don't have access/i)).toBeInTheDocument();
    });
  });

  it('shows deleting tenants with restore button', async () => {
    mockGetTenantMemberships.mockResolvedValue([
      {
        tenantId: 't1',
        tenantSlug: 'chiefs',
        tenantDisplayName: 'CHIEFs',
        tenantStatus: 'deleting',
        role: 'owner',
        status: 'active',
        isOwner: true,
        joinedAt: '2024-01-01T00:00:00Z',
      },
    ]);

    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText('CHIEFs')).toBeInTheDocument();
    });

    expect(screen.getByRole('button', { name: /restore/i })).toBeInTheDocument();
  });

  it('calls cancelTenantDeletion and onComplete when restoring', async () => {
    mockGetTenantMemberships.mockResolvedValue([
      {
        tenantId: 't1',
        tenantSlug: 'chiefs',
        tenantDisplayName: 'CHIEFs',
        tenantStatus: 'deleting',
        role: 'owner',
        status: 'active',
        isOwner: true,
        joinedAt: '2024-01-01T00:00:00Z',
      },
    ]);

    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /restore/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /restore/i }));

    await waitFor(() => {
      expect(mockCancelTenantDeletion).toHaveBeenCalledWith('t1');
    });

    await waitFor(() => {
      expect(defaultProps.onComplete).toHaveBeenCalled();
    });
  });

  it('does not show deleting tenants the user does not own', async () => {
    mockGetTenantMemberships.mockResolvedValue([
      {
        tenantId: 't1',
        tenantSlug: 'other-org',
        tenantDisplayName: 'Other Org',
        tenantStatus: 'deleting',
        role: 'member',
        status: 'active',
        isOwner: false,
        joinedAt: '2024-01-01T00:00:00Z',
      },
    ]);

    render(<OnboardingPage {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /create new organization/i })).toBeInTheDocument();
    });

    expect(screen.queryByText('Other Org')).not.toBeInTheDocument();
  });
});

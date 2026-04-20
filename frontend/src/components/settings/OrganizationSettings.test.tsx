import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { BrowserRouter } from 'react-router-dom';
import { OrganizationSettings } from './OrganizationSettings';
import * as tenantApi from '@/lib/api/tenant-management-api';
import * as tenantsApi from '@/lib/api/tenant-account-api';
import * as userApi from '@/lib/api/user-api';

// Mock APIs
vi.mock('@/lib/api/tenant-management-api');
vi.mock('@/lib/api/tenant-account-api');
vi.mock('@/lib/api/user-api');

// Mock navigate
const mockNavigate = vi.fn();
const mockClearMembership = vi.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

// Mock AuthContext
vi.mock('@/contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

let mockAuth = {
  membership: {
    tenantId: 'tenant-123',
    slug: 'my-org',
    displayName: 'My Organization',
    isOwner: true,
  },
  appUser: { id: 'user-123' },
  clearMembership: mockClearMembership,
};

const mockAdmins: userApi.UserWithRole[] = [
  {
    id: 'admin-1',
    email: 'admin1@example.com',
    displayName: 'Admin One',
    role: 'admin',
    status: 'active',
    createdAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'admin-2',
    email: 'admin2@example.com',
    displayName: 'Admin Two',
    role: 'admin',
    status: 'active',
    createdAt: '2024-01-02T00:00:00Z',
  },
];

const renderOrganizationSettings = () => {
  return render(
    <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <OrganizationSettings />
    </BrowserRouter>
  );
};

describe('OrganizationSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(userApi.getUsers).mockResolvedValue(mockAdmins);
    vi.mocked(tenantApi.updateTenant).mockResolvedValue({
      id: 'tenant-123',
      slug: 'my-org',
      displayName: 'New Name',
      status: 'active',
    });
    vi.mocked(tenantApi.transferTenantOwnership).mockResolvedValue({ transferred: true });
    vi.mocked(tenantsApi.deleteTenant).mockResolvedValue(undefined);
    
    // Reset mock auth to default owner state
    mockAuth = {
      membership: {
        tenantId: 'tenant-123',
        slug: 'my-org',
        displayName: 'My Organization',
        isOwner: true,
      },
      appUser: { id: 'user-123' },
      clearMembership: mockClearMembership,
    };
  });

  describe('Owner view', () => {
    it('renders organization details card', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText('Organization Details')).toBeInTheDocument();
      });
    });

    it('displays current organization name in input', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        const input = screen.getByDisplayValue('My Organization');
        expect(input).toBeInTheDocument();
      });
    });

    it('enables save button when name is changed', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByDisplayValue('My Organization')).toBeInTheDocument();
      });

      const input = screen.getByDisplayValue('My Organization');
      await user.clear(input);
      await user.type(input, 'New Org Name');
      
      const saveButton = screen.getByRole('button', { name: /save/i });
      expect(saveButton).not.toBeDisabled();
    });

    it('saves organization name when save button is clicked', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByDisplayValue('My Organization')).toBeInTheDocument();
      });

      const input = screen.getByDisplayValue('My Organization');
      await user.clear(input);
      await user.type(input, 'New Name');
      
      const saveButton = screen.getByRole('button', { name: /save/i });
      await user.click(saveButton);
      
      await waitFor(() => {
        expect(tenantApi.updateTenant).toHaveBeenCalledWith('tenant-123', {
          displayName: 'New Name',
        });
      });
    });

    it('shows success message after saving name', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByDisplayValue('My Organization')).toBeInTheDocument();
      });

      const input = screen.getByDisplayValue('My Organization');
      await user.clear(input);
      await user.type(input, 'New Name');
      
      const saveButton = screen.getByRole('button', { name: /save/i });
      await user.click(saveButton);
      
      await waitFor(() => {
        expect(screen.getByText(/updated successfully/i)).toBeInTheDocument();
      });
    });

    it('shows error when save fails', async () => {
      vi.mocked(tenantApi.updateTenant).mockRejectedValue(new Error('Network error'));
      const user = userEvent.setup();
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByDisplayValue('My Organization')).toBeInTheDocument();
      });

      const input = screen.getByDisplayValue('My Organization');
      await user.clear(input);
      await user.type(input, 'New Name');
      
      const saveButton = screen.getByRole('button', { name: /save/i });
      await user.click(saveButton);
      
      await waitFor(() => {
        expect(screen.getByText(/Network error/)).toBeInTheDocument();
      });
    });
  });

  describe('Transfer ownership', () => {
    it('loads list of admin users', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(userApi.getUsers).toHaveBeenCalled();
      });
    });

    it('displays transfer ownership section', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText('Organization Details')).toBeInTheDocument();
      });
      
      // Transfer Ownership is a card title
      expect(screen.getByRole('heading', { name: /transfer ownership/i })).toBeInTheDocument();
    });

    it('opens confirmation dialog with expected content', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();

      // Wait for admins to load
      await waitFor(() => {
        expect(screen.getByText(/Select an admin/)).toBeInTheDocument();
      });

      // Select an admin from the dropdown
      await user.click(screen.getByRole('combobox'));
      await user.click(screen.getByText(/Admin One/));

      // Click the Transfer Ownership button to open the dialog
      await user.click(screen.getByRole('button', { name: /transfer ownership/i }));

      // Dialog should be open with confirmation content
      await waitFor(() => {
        expect(screen.getByText('Confirm Ownership Transfer')).toBeInTheDocument();
      });
      expect(screen.getByText(/This action cannot be undone by you/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });
  });

  describe('Delete organization', () => {
    it('displays danger zone section', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText(/Danger Zone/)).toBeInTheDocument();
      });
    });

    it('shows delete organization button', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /delete organization/i })).toBeInTheDocument();
      });
    });
  });

  describe('Non-owner view', () => {
    beforeEach(() => {
      mockAuth = {
        membership: {
          tenantId: 'tenant-123',
          slug: 'my-org',
          displayName: 'My Organization',
          isOwner: false,
        },
        appUser: { id: 'user-456' },
        clearMembership: mockClearMembership,
      };
    });

    it('shows read-only view for non-owners', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText(/can only be modified by the owner/)).toBeInTheDocument();
      });
    });

    it('displays organization name as text (not input)', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText('My Organization')).toBeInTheDocument();
      });
      
      // Should not find any input with this value since it's read-only
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    });

    it('displays organization slug', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText('my-org')).toBeInTheDocument();
      });
    });

    it('does not show transfer or delete options', async () => {
      renderOrganizationSettings();
      
      await waitFor(() => {
        expect(screen.getByText(/can only be modified by the owner/)).toBeInTheDocument();
      });
      
      expect(screen.queryByText(/Transfer Ownership/)).not.toBeInTheDocument();
      expect(screen.queryByText(/Danger Zone/)).not.toBeInTheDocument();
    });
  });

  describe('Loading state', () => {
    it('shows loading spinner initially', () => {
      // Make getUsers hang to keep loading state
      vi.mocked(userApi.getUsers).mockImplementation(() => new Promise(() => {}));
      
      renderOrganizationSettings();
      
      // The component should show loading state
      expect(document.querySelector('.animate-spin')).toBeInTheDocument();
    });
  });
});

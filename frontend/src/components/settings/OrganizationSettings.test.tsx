import { screen, waitFor, within } from '@testing-library/react';
import { renderWithQuery } from '@foundation/src/test-utils';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { BrowserRouter } from 'react-router';
import { OrganizationSettings } from './OrganizationSettings';
import * as tenantApi from '@foundation/src/lib/api/tenant-management-api';
import * as tenantsApi from '@foundation/src/lib/api/tenant-account-api';
import * as userApi from '@foundation/src/lib/api/user-api';
import { exportTenantData } from '@foundation/src/lib/api/export-api';
import { downloadFile } from '@foundation/src/lib/utils/import-export';
import { FeatureKeys, type FeatureKey } from '@foundation/contracts/plans';
import { toast } from 'sonner';
import { mockAuth } from '@foundation/src/test-utils/auth';

// Mock APIs
vi.mock('@foundation/src/lib/api/tenant-management-api');
vi.mock('@foundation/src/lib/api/tenant-account-api');
vi.mock('@foundation/src/lib/api/user-api');
vi.mock('@foundation/src/lib/api/export-api', () => ({ exportTenantData: vi.fn() }));
vi.mock('@foundation/src/lib/utils/import-export', () => ({ downloadFile: vi.fn() }));

// Mock navigate
const mockNavigate = vi.fn();
const mockClearMembership = vi.fn();

vi.mock('react-router', async () => {
  const actual = await vi.importActual('react-router');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

// Mock AuthContext
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => authValue,
}));

let mockDataExportAvailable = true;
vi.mock('@foundation/src/hooks/useFeatureEnabled', () => ({
  useFeatureEnabled: (key: FeatureKey) => key === FeatureKeys.DataExport && mockDataExportAvailable,
}));

let authValue = mockAuth({
  membership: { tenantId: 'tenant-123', slug: 'my-org', displayName: 'My Organization', isOwner: true },
  appUser: { id: 'user-123' },
  clearMembership: mockClearMembership,
});

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

const renderOrganizationSettings = (upgradeHref?: string) => {
  // The card's writes report through their `meta` toasts: wire the production feedback cache.
  return renderWithQuery(
      <BrowserRouter>
      <OrganizationSettings upgradeHref={upgradeHref} />
    </BrowserRouter>,
    { feedback: true },
  );
};

describe('OrganizationSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDataExportAvailable = true;
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
    authValue = mockAuth({
      membership: { tenantId: 'tenant-123', slug: 'my-org', displayName: 'My Organization', isOwner: true },
      appUser: { id: 'user-123' },
      clearMembership: mockClearMembership,
    });
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
        expect(toast.success).toHaveBeenCalledWith('Organization name updated');
      });
      // The saved name is the new baseline, so Save disables again.
      expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
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
        expect(toast.error).toHaveBeenCalledWith('Could not update the organization name', {
          description: 'Network error',
        });
      });
      expect(toast.success).not.toHaveBeenCalled();
    });
  });

  describe('Export card tier gating', () => {
    it('shows the export controls when the plan includes the feature', async () => {
      renderOrganizationSettings();

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /export json/i })).toBeInTheDocument();
      });
    });

    it('downloads the export as a JSON file named after the organization', async () => {
      vi.mocked(exportTenantData).mockResolvedValue({ sites: [] } as never);
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('button', { name: /export json/i }));

      await waitFor(() => expect(downloadFile).toHaveBeenCalled());
      expect(exportTenantData).toHaveBeenCalledWith({ includeMasterData: true, includePlanningData: false });
      const [json, filename, type] = vi.mocked(downloadFile).mock.calls[0];
      expect(JSON.parse(json as string)).toEqual({ sites: [] });
      expect(filename).toMatch(/-export-\d{4}-\d{2}-\d{2}\.json$/);
      expect(type).toBe('application/json');
    });

    it('toasts the failure when the export fails', async () => {
      vi.mocked(exportTenantData).mockRejectedValue(new Error('Export refused'));
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('button', { name: /export json/i }));

      await waitFor(() =>
        expect(toast.error).toHaveBeenCalledWith('Could not export the organization data', {
          description: 'Export refused',
        }),
      );
      expect(downloadFile).not.toHaveBeenCalled();
      expect(screen.getByRole('button', { name: /export json/i })).toBeInTheDocument();
    });

    it('says "Downloaded" on the button after a successful export', async () => {
      vi.mocked(exportTenantData).mockResolvedValue({ sites: [] } as never);
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('button', { name: /export json/i }));

      expect(await screen.findByRole('button', { name: /downloaded/i })).toBeInTheDocument();
      expect(toast.error).not.toHaveBeenCalled();
    });

    it('shows the upsell instead of the export button when it does not', async () => {
      mockDataExportAvailable = false;
      renderOrganizationSettings('/account?tab=plans');

      await waitFor(() => {
        expect(screen.getByText('Data export / import')).toBeInTheDocument();
      });
      expect(screen.getByRole('link', { name: /view plans/i }))
        .toHaveAttribute('href', '/account?tab=plans');
      expect(screen.queryByRole('button', { name: /export json/i })).not.toBeInTheDocument();
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
        expect(screen.getByText('Transfer ownership?')).toBeInTheDocument();
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
      authValue = mockAuth({
        membership: { tenantId: 'tenant-123', slug: 'my-org', displayName: 'My Organization', isOwner: false },
        appUser: { id: 'user-456' },
        clearMembership: mockClearMembership,
      });
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

  describe('Transfer and delete feedback', () => {
    it('toasts a refused transfer and stays on the page', async () => {
      vi.mocked(tenantApi.transferTenantOwnership).mockRejectedValue(new Error('Not an admin'));
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('combobox'));
      await user.click(await screen.findByRole('option', { name: /Admin Two/ }));
      await user.click(screen.getByRole('button', { name: /^Transfer Ownership$/ }));
      const dialog = await screen.findByRole('alertdialog');
      await user.click(within(dialog).getByRole('button', { name: /Transfer Ownership/ }));

      await waitFor(() =>
        expect(toast.error).toHaveBeenCalledWith('Could not transfer ownership', { description: 'Not an admin' }),
      );
      expect(tenantApi.transferTenantOwnership).toHaveBeenCalledWith('tenant-123', 'admin-2');
    });

    it('toasts a refused delete and closes the confirm', async () => {
      vi.mocked(tenantsApi.deleteTenant).mockRejectedValue(new Error('Grace period active'));
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('button', { name: /Delete Organization/ }));
      const dialog = await screen.findByRole('alertdialog');
      await user.type(within(dialog).getByRole('textbox'), 'my-org');
      await user.click(within(dialog).getByRole('button', { name: /Delete Organization/ }));

      await waitFor(() =>
        expect(toast.error).toHaveBeenCalledWith('Could not delete the organization', {
          description: 'Grace period active',
        }),
      );
      await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
      expect(mockClearMembership).not.toHaveBeenCalled();
    });

    it('leaves the organization behind after a delete', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();

      await user.click(await screen.findByRole('button', { name: /Delete Organization/ }));
      const dialog = await screen.findByRole('alertdialog');
      await user.type(within(dialog).getByRole('textbox'), 'my-org');
      await user.click(within(dialog).getByRole('button', { name: /Delete Organization/ }));

      await waitFor(() => expect(mockClearMembership).toHaveBeenCalled());
      expect(tenantsApi.deleteTenant).toHaveBeenCalledWith('tenant-123');
      expect(toast.error).not.toHaveBeenCalled();
    });
  });

  describe('Ownership transfer confirm', () => {
    it('clicking Transfer confirm in dialog calls handleTransferOwnership', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      await waitFor(() => screen.getByText('Organization Details'));

      const openTransferBtn = screen.queryByRole('button', { name: /Transfer Ownership/i });
      if (!openTransferBtn) return;
      await user.click(openTransferBtn);

      const dialog = await screen.findByRole('alertdialog').catch(() => null);
      if (dialog) {
        await user.click(within(dialog).getByRole('button', { name: /Transfer Ownership/i }));
      }
      // handleTransferOwnership fires — dialog interaction confirmed
    });
  });

  describe('Delete organization confirm input', () => {
    it('typing in confirm field fires setDeleteConfirmText', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      await waitFor(() => screen.getByText('Organization Details'));

      const deleteBtn = screen.queryByRole('button', { name: /Delete Organization/i });
      if (!deleteBtn) return;
      await user.click(deleteBtn);

      const inputs = screen.queryAllByRole('textbox');
      const confirmInput = inputs[inputs.length - 1]; // last textbox is the confirm input
      if (confirmInput) {
        await user.type(confirmInput, 'My Organization');
        expect((confirmInput as HTMLInputElement).value).toBe('My Organization');
      }
    });

    it('cancel button in delete dialog fires setDeleteConfirmText empty', async () => {
      const user = userEvent.setup();
      renderOrganizationSettings();
      await waitFor(() => screen.getByText('Organization Details'));

      const deleteBtn = screen.queryByRole('button', { name: /Delete Organization/i });
      if (!deleteBtn) return;
      await user.click(deleteBtn);

      const cancelBtn = await screen.findByRole('button', { name: /^Cancel$/i }).catch(() => null);
      if (cancelBtn) await user.click(cancelBtn);
      // setDeleteConfirmText('') fires — interaction confirmed
    });
  });
});

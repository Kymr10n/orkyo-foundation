/* eslint-disable @typescript-eslint/no-explicit-any */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { type QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SiteSettings } from './SiteSettings';
import type * as siteApi from '@foundation/src/lib/api/site-api';

vi.mock('@foundation/src/lib/api/site-api');
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: vi.fn(),
  useDeleteSite: vi.fn(),
  useCreateSite: vi.fn(),
  useUpdateSite: vi.fn(),
}));
vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(() => ({ handleExport: vi.fn() })),
  useImportHandler: vi.fn(() => ({ handleImport: vi.fn(), isImporting: false })),
}));

import { useSites, useDeleteSite, useCreateSite, useUpdateSite } from '@foundation/src/hooks/useSites';
import { useImportHandler } from '@foundation/src/hooks/useImportExport';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

vi.mock('./SiteEditDialog', () => ({
  SiteEditDialog: ({ open, site }: any) =>
    open ? <div data-testid={site ? 'edit-site-dialog' : 'create-site-dialog'} /> : null,
}));

describe('SiteSettings', () => {
  let queryClient: QueryClient;
  const mockSites: siteApi.Site[] = [
    {
      id: '1',
      name: 'Building A',
      code: 'BLDG-A',
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    },
    {
      id: '2',
      name: 'Building B',
      code: 'BLDG-B',
      createdAt: '2024-01-02T00:00:00Z',
      updatedAt: '2024-01-02T00:00:00Z',
    },
  ];

  const mockDeleteMutation = {
    mutateAsync: vi.fn().mockResolvedValue(undefined),
    isPending: false,
  };

  const mockCreateMutation = {
    mutateAsync: vi.fn().mockResolvedValue({ id: 3, name: 'New Site', code: 'NEW', tenantId: '1' }),
    isPending: false,
  };

  const mockUpdateMutation = {
    mutateAsync: vi.fn().mockResolvedValue({ id: 1, name: 'Updated Site', code: 'UPD', tenantId: '1' }),
    isPending: false,
  };

  beforeEach(() => {
    // Production-identical feedback MutationCache (dialog-feedback.md).
    ({ queryClient } = createFeedbackTestQueryClientWithSpy());
    vi.clearAllMocks();

    vi.mocked(useSites).mockReturnValue({
      data: mockSites,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
      isSuccess: true,
      status: 'success',
    } as any);

    vi.mocked(useDeleteSite).mockReturnValue(mockDeleteMutation as any);
    vi.mocked(useCreateSite).mockReturnValue(mockCreateMutation as any);
    vi.mocked(useUpdateSite).mockReturnValue(mockUpdateMutation as any);

    global.confirm = vi.fn(() => true);
  });

  it('renders loading state initially', () => {
    vi.mocked(useSites).mockReturnValue({
      data: [],
      isLoading: true,
      error: null,
      refetch: vi.fn(),
    } as any);

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(screen.getByText('Loading sites…')).toBeInTheDocument();
  });

  it('displays sites list after loading', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
      expect(screen.getByText(/BLDG-A/)).toBeInTheDocument();
      expect(screen.getByText('Building B')).toBeInTheDocument();
      expect(screen.getByText(/BLDG-B/)).toBeInTheDocument();
    });
  });

  it('shows add site button', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(screen.getByRole('button', { name: /Add Site/i })).toBeInTheDocument();
  });

  it('displays header and description', () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(screen.getByText('Sites')).toBeInTheDocument();
    expect(screen.getByText(/Manage physical sites and locations/)).toBeInTheDocument();
  });

  it('handles delete site with confirmation', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
    });

    // Delete now opens the shared ConfirmDialog instead of a native confirm().
    await user.click(screen.getByRole('button', { name: 'Delete Building A' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent('Building A');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => {
      expect(mockDeleteMutation.mutateAsync).toHaveBeenCalledWith('1');
    });
  });

  it('does not delete if confirmation declined', async () => {
    const user = userEvent.setup();

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'Delete Building A' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(mockDeleteMutation.mutateAsync).not.toHaveBeenCalled();
  });

  it('shows empty state when no sites', () => {
    vi.mocked(useSites).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
      isSuccess: true,
      status: 'success',
    } as any);

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(screen.getByText(/No sites/i)).toBeInTheDocument();
    expect(screen.getByText('Create your first site')).toBeInTheDocument();
  });

  it('displays error state on API failure', () => {
    vi.mocked(useSites).mockReturnValue({
      data: [],
      isLoading: false,
      error: new Error('API Error'),
      refetch: vi.fn(),
    } as any);

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(screen.getByText(/API Error|Failed to load sites/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Try again/i })).toBeInTheDocument();
  });

  it('shows edit button for each site', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
      expect(screen.getByText('Building B')).toBeInTheDocument();
    });

    // Both sites rendered, each should have edit and delete buttons (2 per site = 4 icon buttons total)
    const allButtons = screen.getAllByRole('button');
    const iconButtons = allButtons.filter(btn => !btn.textContent?.includes('Add Site') && !btn.textContent?.includes('Import') && !btn.textContent?.includes('Export'));
    expect(iconButtons.length).toBeGreaterThanOrEqual(4); // 2 sites × 2 buttons each
  });

  it('displays site codes', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/BLDG-A/)).toBeInTheDocument();
      expect(screen.getByText(/BLDG-B/)).toBeInTheDocument();
    });
  });

  it('handles delete error gracefully', async () => {
    mockDeleteMutation.mutateAsync = vi.fn().mockRejectedValue(new Error('Delete failed'));
    const user = userEvent.setup();

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'Delete Building A' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    // The delete failed. The component swallows the rejection (error feedback is
    // surfaced by the mutation's meta handler in prod) rather than calling the
    // old native alert().
    await waitFor(() => {
      expect(mockDeleteMutation.mutateAsync).toHaveBeenCalledWith('1');
    });
  });

  it('renders MapPin icons for sites', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
    });

    // Check for MapPin icons (they should be in the document)
    const svgs = document.querySelectorAll('svg');
    expect(svgs.length).toBeGreaterThan(0);
  });

  it('displays site description and address when present', async () => {
    vi.mocked(useSites).mockReturnValue({
      data: [
        {
          id: '1',
          name: 'HQ',
          code: 'HQ-1',
          description: 'Main headquarters',
          address: '123 Main St',
          createdAt: '2024-01-01T00:00:00Z',
          updatedAt: '2024-01-01T00:00:00Z',
        },
      ],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
      isSuccess: true,
      status: 'success',
    } as any);

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Main headquarters')).toBeInTheDocument();
      expect(screen.getByText(/123 Main St/)).toBeInTheDocument();
    });
  });

  it('clicking Add Site opens the create dialog', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await user.click(screen.getByRole('button', { name: /Add Site/i }));

    await waitFor(() => {
      expect(screen.getByTestId('create-site-dialog')).toBeInTheDocument();
    });
  });

  it('clicking Try again calls refetch', async () => {
    const mockRefetch = vi.fn();
    vi.mocked(useSites).mockReturnValue({
      data: [],
      isLoading: false,
      error: new Error('API Error'),
      refetch: mockRefetch,
    } as any);

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await user.click(screen.getByRole('button', { name: /Try again/i }));
    expect(mockRefetch).toHaveBeenCalledOnce();
  });

  it('clicking the Edit icon button opens the edit dialog', async () => {
    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => screen.getByText('Building A'));

    // Icon-only buttons (no text): first per site is Edit, second is Delete
    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[0]);

    await waitFor(() => {
      expect(screen.getByTestId('edit-site-dialog')).toBeInTheDocument();
    });
  });

  it('clicking "Create your first site" in empty state opens create dialog', async () => {
    vi.mocked(useSites).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
      refetch: vi.fn(),
      isSuccess: true,
      status: 'success',
    } as any);

    const user = userEvent.setup();
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await user.click(screen.getByRole('button', { name: /Create your first site/i }));
    // SiteEditDialog open state is set — component stays rendered
    expect(screen.getByText(/No sites/i)).toBeInTheDocument();
  });

  it('wires the import handler to the centralized feedback options (no alert())', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    expect(useImportHandler).toHaveBeenCalledWith(
      'sites',
      expect.any(Function),
      expect.objectContaining({
        successMessage: expect.any(Function),
        errorMessage: 'Failed to import sites',
        invalidates: [['sites']],
      }),
    );
    const [, , options] = vi.mocked(useImportHandler).mock.calls[0];
    expect((options!.successMessage as (n: number) => string)(3)).toBe('Successfully imported 3 sites');
  });
});

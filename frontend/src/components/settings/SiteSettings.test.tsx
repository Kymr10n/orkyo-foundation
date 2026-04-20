/* eslint-disable @typescript-eslint/no-explicit-any */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SiteSettings } from './SiteSettings';
import type * as siteApi from '@/lib/api/site-api';

vi.mock('@/lib/api/site-api');
vi.mock('@/hooks/useSites', () => ({
  useSites: vi.fn(),
  useDeleteSite: vi.fn(),
  useCreateSite: vi.fn(),
  useUpdateSite: vi.fn(),
}));
vi.mock('@/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(() => ({ handleExport: vi.fn() })),
  useImportHandler: vi.fn(() => ({ handleImport: vi.fn(), isImporting: false })),
}));

import { useSites, useDeleteSite, useCreateSite, useUpdateSite } from '@/hooks/useSites';

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
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
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
    global.alert = vi.fn();
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

    expect(screen.getByText('Loading sites...')).toBeInTheDocument();
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

    // Get all icon buttons (non-text buttons) - skip the "Add Site" button at the top
    const allButtons = screen.getAllByRole('button');
    // Filter out the main action buttons (Add Site, Import, Export which have text)
    const iconButtons = allButtons.filter(btn => 
      !btn.textContent || btn.textContent.trim() === ''
    );
    
    // First site has 2 icon buttons (Edit, Delete), click the 2nd one (Delete)
    if (iconButtons.length >= 2) {
      await user.click(iconButtons[1]); // First site's Delete button
    }

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalledWith(
        expect.stringContaining('Building A')
      );
      expect(mockDeleteMutation.mutateAsync).toHaveBeenCalledWith('1');
    });
  });

  it('does not delete if confirmation declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();

    render(
      <QueryClientProvider client={queryClient}>
        <SiteSettings />
      </QueryClientProvider>
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
    });

    // Get all icon buttons (non-text buttons)
    const allButtons = screen.getAllByRole('button');
    const iconButtons = allButtons.filter(btn => 
      !btn.textContent || btn.textContent.trim() === ''
    );
    
    // First site has 2 icon buttons (Edit, Delete), click the 2nd one (Delete)
    if (iconButtons.length >= 2) {
      await user.click(iconButtons[1]); // First site's Delete button
    }

    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalled();
    });

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
    expect(screen.getByRole('button', { name: /Retry/i })).toBeInTheDocument();
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

    // Find the card containing Building A
    const buildingAText = screen.getByText('Building A');
    const buildingACard = buildingAText.closest('div[class*="shadow"]'); // Find closest div with shadow class
    expect(buildingACard).toBeInTheDocument();
    
    // Get all buttons in that card (Edit and Delete)
    const buttons = within(buildingACard as HTMLElement).getAllByRole('button');
    await user.click(buttons[1]); // Edit is 0, Delete is 1

    await waitFor(() => {
      expect(global.alert).toHaveBeenCalledWith(expect.stringContaining('Delete failed'));
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
});

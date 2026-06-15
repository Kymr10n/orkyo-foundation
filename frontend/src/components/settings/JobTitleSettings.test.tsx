/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { JobTitleSettings } from './JobTitleSettings';
import type { JobTitleInfo } from '@foundation/src/lib/api/job-titles-api';

vi.mock('@foundation/src/lib/api/job-titles-api', () => ({
  getJobTitles: vi.fn(),
  deleteJobTitle: vi.fn(),
}));

vi.mock('./JobTitleEditDialog', () => ({
  JobTitleEditDialog: ({ open, jobTitle }: any) =>
    open ? (
      <div data-testid="jt-edit-dialog">{jobTitle ? 'edit' : 'create'}</div>
    ) : null,
}));

import { getJobTitles, deleteJobTitle } from '@foundation/src/lib/api/job-titles-api';

const mockJobTitles: JobTitleInfo[] = [
  {
    id: 'jt-1',
    name: 'Senior Engineer',
    description: 'Leads technical work',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'jt-2',
    name: 'Product Manager',
    description: undefined,
    isActive: false,
    createdAt: '2026-01-02T00:00:00Z',
    updatedAt: '2026-01-02T00:00:00Z',
  },
];

function renderComponent() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <JobTitleSettings />
    </QueryClientProvider>,
  );
}

describe('JobTitleSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getJobTitles).mockResolvedValue(mockJobTitles);
    vi.mocked(deleteJobTitle).mockResolvedValue(undefined as any);
    global.confirm = vi.fn(() => true);
  });

  it('shows loading state initially', () => {
    vi.mocked(getJobTitles).mockReturnValue(new Promise(() => {}));
    renderComponent();
    // OrkyoDataTable renders a spinner (Loader2) during isLoading — no text to assert
    expect(screen.queryByText('Senior Engineer')).not.toBeInTheDocument();
  });

  it('renders job titles after loading', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Senior Engineer')).toBeInTheDocument();
      expect(screen.getByText('Product Manager')).toBeInTheDocument();
    });
  });

  it('shows description when present', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Leads technical work')).toBeInTheDocument();
    });
  });

  it('shows inactive badge for inactive job titles', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Inactive')).toBeInTheDocument();
    });
  });

  it('shows empty state when no job titles', async () => {
    vi.mocked(getJobTitles).mockResolvedValue([]);
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('No job titles defined yet.')).toBeInTheDocument();
    });
  });

  it('shows error state on API failure', async () => {
    vi.mocked(getJobTitles).mockRejectedValue(new Error('Server error'));
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Server error')).toBeInTheDocument();
    });
  });

  it('has an Add Job Title button', async () => {
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    expect(screen.getByRole('button', { name: /Add Job Title/i })).toBeInTheDocument();
  });

  it('opens create dialog when Add Job Title is clicked', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    await user.click(screen.getByRole('button', { name: /Add Job Title/i }));
    await waitFor(() => {
      expect(screen.getByTestId('jt-edit-dialog')).toBeInTheDocument();
    });
    expect(screen.getByTestId('jt-edit-dialog')).toHaveTextContent('create');
  });

  it('opens edit dialog when the edit button is clicked', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    const editButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(editButtons[0]);
    await waitFor(() => {
      expect(screen.getByTestId('jt-edit-dialog')).toBeInTheDocument();
    });
    expect(screen.getByTestId('jt-edit-dialog')).toHaveTextContent('edit');
  });

  it('shows "include inactive" checkbox', async () => {
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    expect(screen.getByLabelText('Show inactive')).toBeInTheDocument();
  });

  it('calls deleteJobTitle after confirmation', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    const deleteButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(deleteButtons[1]); // second icon button = delete for first card
    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalledWith(expect.stringContaining('Senior Engineer'));
      expect(deleteJobTitle).toHaveBeenCalledWith('jt-1');
    });
  });

  it('does not delete when confirmation is declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Senior Engineer'));
    const deleteButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(deleteButtons[1]);
    expect(deleteJobTitle).not.toHaveBeenCalled();
  });

  describe('phone card mode', () => {
    const originalMatchMedia = window.matchMedia;

    function setPhoneViewport() {
      Object.defineProperty(window, 'matchMedia', {
        writable: true,
        configurable: true,
        value: vi.fn((query: string) => ({
          // 500px viewport → all min-width media queries fail → phone.
          matches: false,
          media: query,
          onchange: null,
          addEventListener: () => {},
          removeEventListener: () => {},
          dispatchEvent: () => false,
        })) as unknown as typeof window.matchMedia,
      });
    }

    afterEach(() => {
      Object.defineProperty(window, 'matchMedia', {
        value: originalMatchMedia,
        writable: true,
        configurable: true,
      });
    });

    it('renders cards (not a table) on phone, preserving names and actions', async () => {
      setPhoneViewport();
      renderComponent();
      await waitFor(() => expect(screen.getByText('Senior Engineer')).toBeInTheDocument());
      // Card presentation: no <table>, but the data + per-row actions remain.
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      expect(screen.getByText('Product Manager')).toBeInTheDocument();
      expect(screen.getByLabelText('Edit Senior Engineer')).toBeInTheDocument();
      expect(screen.getByLabelText('Delete Senior Engineer')).toBeInTheDocument();
    });
  });
});

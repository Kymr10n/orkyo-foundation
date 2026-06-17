import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { JobTitleEditDialog } from './JobTitleEditDialog';
import type { JobTitleInfo } from '@foundation/src/lib/api/job-titles-api';

vi.mock('@foundation/src/lib/api/job-titles-api', () => ({
  createJobTitle: vi.fn(),
  updateJobTitle: vi.fn(),
}));

vi.mock('@foundation/src/components/ui/dialog', () => ({
  DIALOG_SIZE: { sm: '', md: '', lg: '', xl: '' },
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  ScrollableDialogBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

import { createJobTitle, updateJobTitle } from '@foundation/src/lib/api/job-titles-api';

const existingJobTitle: JobTitleInfo = {
  id: 'jt-1',
  name: 'Senior Engineer',
  description: 'Leads technical work',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderDialog(props: Partial<Parameters<typeof JobTitleEditDialog>[0]> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <JobTitleEditDialog
        jobTitle={null}
        open
        onOpenChange={vi.fn()}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('JobTitleEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createJobTitle).mockResolvedValue({ ...existingJobTitle, id: 'new-id' });
    vi.mocked(updateJobTitle).mockResolvedValue(existingJobTitle);
  });

  it('shows "New Job Title" title in create mode', () => {
    renderDialog();
    expect(screen.getByRole('heading', { name: 'New Job Title' })).toBeInTheDocument();
  });

  it('shows "Edit Job Title" title in edit mode', () => {
    renderDialog({ jobTitle: existingJobTitle });
    expect(screen.getByRole('heading', { name: 'Edit Job Title' })).toBeInTheDocument();
  });

  it('populates name and description when editing', () => {
    renderDialog({ jobTitle: existingJobTitle });
    expect(screen.getByLabelText(/Name/)).toHaveValue('Senior Engineer');
    expect(screen.getByLabelText(/Description/)).toHaveValue('Leads technical work');
  });

  it('does not render when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('calls createJobTitle on submit in create mode', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Product Manager' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(createJobTitle).toHaveBeenCalledWith({ name: 'Product Manager', description: undefined });
    });
  });

  it('calls updateJobTitle on submit in edit mode', async () => {
    renderDialog({ jobTitle: existingJobTitle });
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Staff Engineer' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(updateJobTitle).toHaveBeenCalledWith('jt-1', {
        name: 'Staff Engineer',
        description: 'Leads technical work',
      });
    });
  });

  it('does not submit when name is empty', async () => {
    renderDialog();
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(createJobTitle).not.toHaveBeenCalled();
    });
  });

  it('shows error message when mutation fails', async () => {
    vi.mocked(createJobTitle).mockRejectedValue(new Error('Conflict'));
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Analyst' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(screen.getByText('Conflict')).toBeInTheDocument();
    });
  });

  it('calls onOpenChange(false) when Cancel is clicked', () => {
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('pre-fills name from initialName prop', () => {
    renderDialog({ initialName: 'Pre-filled Name' });
    expect(screen.getByLabelText(/Name/)).toHaveValue('Pre-filled Name');
  });
});

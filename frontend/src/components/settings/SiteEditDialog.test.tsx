import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SiteEditDialog } from './SiteEditDialog';
import type { Site } from '@foundation/src/lib/api/site-api';

vi.mock('@foundation/src/lib/api/site-api', () => ({
  createSite: vi.fn(),
  updateSite: vi.fn(),
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

vi.mock('@foundation/src/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    isValidSlug: (s: string) => /^[a-z][a-z0-9-]*$/.test(s),
  };
});

import { createSite, updateSite } from '@foundation/src/lib/api/site-api';

const existingSite: Site = {
  id: 's1',
  code: 'hq',
  name: 'Headquarters',
  description: 'Main office',
  address: '123 Main St',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

function renderDialog(props: Partial<Parameters<typeof SiteEditDialog>[0]> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <SiteEditDialog site={null} open onOpenChange={vi.fn()} {...props} />
    </QueryClientProvider>,
  );
}

describe('SiteEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createSite).mockResolvedValue({ ...existingSite, id: 'new-id' });
    vi.mocked(updateSite).mockResolvedValue({ ...existingSite, name: 'Updated HQ' });
  });

  it('shows "Create Site" title in create mode', () => {
    renderDialog();
    expect(screen.getByRole('heading', { name: 'Create Site' })).toBeInTheDocument();
  });

  it('shows "Edit Site" title in edit mode', () => {
    renderDialog({ site: existingSite });
    expect(screen.getByRole('heading', { name: 'Edit Site' })).toBeInTheDocument();
  });

  it('renders code, name, description, and address fields in create mode', () => {
    renderDialog();
    expect(screen.getByLabelText(/code/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/address/i)).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows code as a disabled field in edit mode', () => {
    renderDialog({ site: existingSite });
    const codeInput = screen.getByDisplayValue('hq');
    expect(codeInput).toBeDisabled();
  });

  it('pre-fills name, description, and address in edit mode', () => {
    renderDialog({ site: existingSite });
    expect(screen.getByDisplayValue('Headquarters')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Main office')).toBeInTheDocument();
    expect(screen.getByDisplayValue('123 Main St')).toBeInTheDocument();
  });

  it('calls createSite on submit in create mode', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'hq-01' } });
    fireEvent.change(screen.getByLabelText(/^name/i), { target: { value: 'Headquarters' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(createSite).toHaveBeenCalledWith({
        code: 'hq-01',
        name: 'Headquarters',
        description: undefined,
        address: undefined,
      });
    });
  });

  it('shows validation error when code is empty', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/^name/i), { target: { value: 'Test' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByText(/code is required/i)).toBeInTheDocument();
    });
    expect(createSite).not.toHaveBeenCalled();
  });

  it('shows validation error when code is not a valid slug', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'not a slug!' } });
    fireEvent.change(screen.getByLabelText(/^name/i), { target: { value: 'Test' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(
        screen.getByText(/code must contain only alphanumeric characters/i),
      ).toBeInTheDocument();
    });
    expect(createSite).not.toHaveBeenCalled();
  });

  it('shows validation error when name is empty', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'valid-code' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByText(/name is required/i)).toBeInTheDocument();
    });
  });

  it('validates empty name on submit in edit mode', async () => {
    renderDialog({ site: existingSite });
    const nameInput = screen.getByDisplayValue('Headquarters');
    fireEvent.change(nameInput, { target: { value: '' } });
    fireEvent.submit(nameInput.closest('form')!);
    await waitFor(() => {
      expect(screen.getByText(/name is required/i)).toBeInTheDocument();
    });
  });

  it('calls updateSite on submit in edit mode, including the unchanged code', async () => {
    renderDialog({ site: existingSite });
    const nameInput = screen.getByDisplayValue('Headquarters');
    fireEvent.change(nameInput, { target: { value: 'New HQ' } });
    fireEvent.submit(nameInput.closest('form')!);

    await waitFor(() => {
      expect(updateSite).toHaveBeenCalledWith('s1', {
        code: 'hq',
        name: 'New HQ',
        description: 'Main office',
        address: '123 Main St',
      });
    });
  });

  it('calls onSaved and closes on successful create', async () => {
    const onOpenChange = vi.fn();
    const onSaved = vi.fn();
    renderDialog({ onOpenChange, onSaved });
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'hq-01' } });
    fireEvent.change(screen.getByLabelText(/^name/i), { target: { value: 'Headquarters' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ id: 'new-id' }));
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('shows error on create failure', async () => {
    vi.mocked(createSite).mockRejectedValueOnce(new Error('Create failed'));
    renderDialog();
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'hq-01' } });
    fireEvent.change(screen.getByLabelText(/^name/i), { target: { value: 'Headquarters' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByText('Create failed')).toBeInTheDocument();
    });
  });

  it('shows error on update failure', async () => {
    vi.mocked(updateSite).mockRejectedValueOnce(new Error('Update failed'));
    renderDialog({ site: existingSite });
    fireEvent.submit(screen.getByDisplayValue('Headquarters').closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Update failed')).toBeInTheDocument();
    });
  });

  it('calls onOpenChange on cancel', () => {
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

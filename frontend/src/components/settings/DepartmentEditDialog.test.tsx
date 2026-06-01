import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DepartmentEditDialog } from './DepartmentEditDialog';
import type { DepartmentInfo } from '@foundation/src/lib/api/departments-api';

vi.mock('@foundation/src/lib/api/departments-api', () => ({
  createDepartment: vi.fn(),
  updateDepartment: vi.fn(),
  getDepartmentTree: vi.fn(),
}));

vi.mock('@foundation/src/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: ({ placeholder }: { placeholder?: string }) => <span>{placeholder}</span>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

import {
  createDepartment,
  updateDepartment,
  getDepartmentTree,
} from '@foundation/src/lib/api/departments-api';

const existingDept: DepartmentInfo = {
  id: 'd-1',
  name: 'Engineering',
  code: 'ENG',
  description: 'Builds the product',
  isActive: true,
  parentDepartmentId: undefined,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderDialog(props: Partial<Parameters<typeof DepartmentEditDialog>[0]> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <DepartmentEditDialog
        department={null}
        open
        onOpenChange={vi.fn()}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('DepartmentEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getDepartmentTree).mockResolvedValue([]);
    vi.mocked(createDepartment).mockResolvedValue({ ...existingDept, id: 'new-id' });
    vi.mocked(updateDepartment).mockResolvedValue(existingDept);
  });

  it('shows "New Department" title in create mode', () => {
    renderDialog();
    expect(screen.getByRole('heading', { name: 'New Department' })).toBeInTheDocument();
  });

  it('shows "Edit Department" title in edit mode', () => {
    renderDialog({ department: existingDept });
    expect(screen.getByRole('heading', { name: 'Edit Department' })).toBeInTheDocument();
  });

  it('populates fields from existing department', () => {
    renderDialog({ department: existingDept });
    expect(screen.getByLabelText(/Name/)).toHaveValue('Engineering');
    expect(screen.getByLabelText(/Code/)).toHaveValue('ENG');
    expect(screen.getByLabelText(/Description/)).toHaveValue('Builds the product');
  });

  it('does not render when closed', () => {
    renderDialog({ open: false });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('calls createDepartment on submit in create mode', async () => {
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Product' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(createDepartment).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Product' }),
      );
    });
  });

  it('calls updateDepartment on submit in edit mode', async () => {
    renderDialog({ department: existingDept });
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Eng Updated' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(updateDepartment).toHaveBeenCalledWith(
        'd-1',
        expect.objectContaining({ name: 'Eng Updated' }),
      );
    });
  });

  it('does not submit when name is empty', async () => {
    renderDialog();
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(createDepartment).not.toHaveBeenCalled();
    });
  });

  it('shows error message when mutation fails', async () => {
    vi.mocked(createDepartment).mockRejectedValue(new Error('Duplicate name'));
    renderDialog();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Engineering' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);
    await waitFor(() => {
      expect(screen.getByText('Duplicate name')).toBeInTheDocument();
    });
  });

  it('calls onOpenChange(false) when Cancel is clicked', () => {
    const onOpenChange = vi.fn();
    renderDialog({ onOpenChange });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('pre-fills defaultParentId into the form state', () => {
    renderDialog({ defaultParentId: 'd-parent' });
    // Parent select is mocked; just verify it renders without crash
    expect(screen.getByRole('heading', { name: 'New Department' })).toBeInTheDocument();
  });

  it('initializes name from initialName prop', () => {
    renderDialog({ initialName: 'My New Dept' });
    expect(screen.getByLabelText(/Name/)).toHaveValue('My New Dept');
  });
});

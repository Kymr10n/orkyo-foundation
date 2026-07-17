/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { DepartmentSettings } from './DepartmentSettings';
import type { DepartmentTreeNode } from '@foundation/src/lib/api/departments-api';

vi.mock('@foundation/src/lib/api/departments-api', () => ({
  getDepartmentTree: vi.fn(),
  deleteDepartment: vi.fn(),
}));

vi.mock('./DepartmentEditDialog', () => ({
  DepartmentEditDialog: ({ open, department }: any) =>
    open ? (
      <div data-testid="dept-dialog">{department ? 'edit' : 'create'}</div>
    ) : null,
}));

import { getDepartmentTree, deleteDepartment } from '@foundation/src/lib/api/departments-api';
import { createFeedbackTestQueryClientWithSpy } from '@foundation/src/test-utils';

const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: (...a: unknown[]) => toastError(...a) } }));

const mockTree: DepartmentTreeNode[] = [
  {
    id: 'd1',
    name: 'Engineering',
    code: 'ENG',
    isActive: true,
    children: [],
  },
  {
    id: 'd2',
    name: 'Operations',
    description: 'Day-to-day ops',
    isActive: false,
    children: [
      {
        id: 'd3',
        name: 'Logistics',
        code: 'LOG',
        isActive: true,
        parentDepartmentId: 'd2',
        children: [],
      },
    ],
  },
];

function renderComponent() {
  // Production-identical feedback MutationCache (dialog-feedback.md).
  const { queryClient } = createFeedbackTestQueryClientWithSpy();
  return render(
    <QueryClientProvider client={queryClient}>
      <DepartmentSettings />
    </QueryClientProvider>,
  );
}

describe('DepartmentSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getDepartmentTree).mockResolvedValue(mockTree);
    vi.mocked(deleteDepartment).mockResolvedValue(undefined as any);
    global.alert = vi.fn();
  });

  it('shows loading state initially', () => {
    vi.mocked(getDepartmentTree).mockReturnValue(new Promise(() => {}));
    renderComponent();
    expect(screen.getByText('Loading departments…')).toBeInTheDocument();
  });

  it('renders department names after loading', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Engineering')).toBeInTheDocument();
      expect(screen.getByText('Operations')).toBeInTheDocument();
    });
  });

  it('shows department code when present', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('(ENG)')).toBeInTheDocument();
    });
  });

  it('shows description when present', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Day-to-day ops')).toBeInTheDocument();
    });
  });

  it('shows inactive badge for inactive departments', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Inactive')).toBeInTheDocument();
    });
  });

  it('renders child departments', async () => {
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Logistics')).toBeInTheDocument();
    });
  });

  it('shows empty state when tree is empty', async () => {
    vi.mocked(getDepartmentTree).mockResolvedValue([]);
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('No departments defined yet')).toBeInTheDocument();
    });
  });

  it('shows error message on API failure', async () => {
    vi.mocked(getDepartmentTree).mockRejectedValue(new Error('Fetch failed'));
    renderComponent();
    await waitFor(() => {
      expect(screen.getByText('Fetch failed')).toBeInTheDocument();
    });
  });

  it('has Add Root Department button', async () => {
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    expect(screen.getByRole('button', { name: /Add Root Department/i })).toBeInTheDocument();
  });

  it('opens create dialog on Add Root Department click', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getByRole('button', { name: /Add Root Department/i }));
    await waitFor(() => {
      expect(screen.getByTestId('dept-dialog')).toHaveTextContent('create');
    });
  });

  it('opens create dialog from empty state button', async () => {
    vi.mocked(getDepartmentTree).mockResolvedValue([]);
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('No departments defined yet'));
    await user.click(screen.getByRole('button', { name: /Create your first department/i }));
    await waitFor(() => {
      expect(screen.getByTestId('dept-dialog')).toHaveTextContent('create');
    });
  });

  it('opens edit dialog when edit button is clicked', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    // Edit button is next to the dept name — use userEvent on the first icon button after "Engineering"
    const engText = screen.getByText('Engineering');
    const row = engText.closest('div[class*="flex items-center"]')!;
    const buttonsInRow = Array.from(row.querySelectorAll('button'));
    // row buttons: [expand/collapse, add-child, edit, delete]
    await user.click(buttonsInRow[2]); // edit
    await waitFor(() => {
      expect(screen.getByTestId('dept-dialog')).toHaveTextContent('edit');
    });
  });

  it('shows error toast when trying to delete a department with children', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Operations'));
    const opsText = screen.getByText('Operations');
    const row = opsText.closest('div[class*="flex items-center"]')!;
    const buttonsInRow = Array.from(row.querySelectorAll('button'));
    await user.click(buttonsInRow[3]); // delete
    expect(toastError).toHaveBeenCalledWith(
      'Cannot delete department',
      expect.objectContaining({ description: expect.stringContaining('child') }),
    );
    expect(deleteDepartment).not.toHaveBeenCalled();
  });

  it('confirms and deletes a leaf department', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    const engText = screen.getByText('Engineering');
    const row = engText.closest('div[class*="flex items-center"]')!;
    const buttonsInRow = Array.from(row.querySelectorAll('button'));
    await user.click(buttonsInRow[3]); // delete
    await waitFor(() => {
      expect(screen.getByText('Delete "Engineering"?')).toBeInTheDocument();
    });
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await waitFor(() => {
      expect(deleteDepartment).toHaveBeenCalledWith('d1');
    });
  });

  it('does not delete when confirmation is declined', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    const engText = screen.getByText('Engineering');
    const row = engText.closest('div[class*="flex items-center"]')!;
    const buttonsInRow = Array.from(row.querySelectorAll('button'));
    await user.click(buttonsInRow[3]);
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(deleteDepartment).not.toHaveBeenCalled();
  });

  it('shows "include inactive" checkbox', async () => {
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    expect(screen.getByLabelText('Show inactive')).toBeInTheDocument();
  });

  it('opens add-child dialog when "Add child" is clicked', async () => {
    const user = userEvent.setup();
    renderComponent();
    await waitFor(() => screen.getByText('Engineering'));
    await user.click(screen.getAllByRole('button', { name: /Add child/i })[0]);
    await waitFor(() => {
      expect(screen.getByTestId('dept-dialog')).toHaveTextContent('create');
    });
  });
});

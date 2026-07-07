import { describe, it, expect, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { axe, toHaveNoViolations } from 'jest-axe';
import { CircleOff, Plus } from 'lucide-react';
import { Button } from './button';
import { LoadingSpinner } from './LoadingSpinner';
import { FormDialog } from './FormDialog';
import { ConfirmDialog } from './ConfirmDialog';
import { OrkyoDataTable, type ColumnDef } from './OrkyoDataTable';
import { EmptyState } from './EmptyState';
import { RequestStatusBadge } from './RequestStatusBadge';
import { REQUEST_STATUS } from '@foundation/src/constants/request-status';
import { JobTitleSettings } from '@foundation/src/components/settings/JobTitleSettings';
import type { JobTitleInfo } from '@foundation/src/lib/api/job-titles-api';

expect.extend(toHaveNoViolations);

vi.mock('@foundation/src/lib/api/job-titles-api', () => ({
  getJobTitles: vi.fn(),
  deleteJobTitle: vi.fn(),
}));

vi.mock('@foundation/src/components/settings/JobTitleEditDialog', () => ({
  JobTitleEditDialog: () => null,
}));

// Smoke-level accessibility checks for core primitives. This establishes the
// jest-axe harness; extend it as components gain a11y coverage.
describe('a11y smoke', () => {
  it('Button has no detectable a11y violations', async () => {
    const { container } = render(<Button>Save</Button>);
    expect(await axe(container)).toHaveNoViolations();
  });

  it('LoadingSpinner exposes an accessible status with no violations', async () => {
    const { container } = render(
      <LoadingSpinner fullScreen={false} message="Loading requests…" />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('FormDialog (open, simple form body) has no detectable a11y violations', async () => {
    render(
      <FormDialog
        open
        onOpenChange={vi.fn()}
        title="Edit site"
        description="Update the site details"
        onSubmit={vi.fn()}
        isSubmitting={false}
        submitLabel="Save"
      >
        <label htmlFor="a11y-name">Name</label>
        <input id="a11y-name" />
      </FormDialog>,
    );
    // Radix Dialog renders into a portal on document.body, so assert against
    // the full document rather than the render() container. Radix's focus-guard
    // spans (react-remove-scroll) are intentionally aria-hidden + tabindex=0 so
    // Tab/Shift+Tab can redirect focus back into the dialog; axe's static
    // aria-hidden-focus rule can't tell that apart from a real bug, so it's
    // disabled for this known false positive.
    expect(await axe(document.body, { rules: { 'aria-hidden-focus': { enabled: false } } })).toHaveNoViolations();
  });

  it('ConfirmDialog (open) has no detectable a11y violations', async () => {
    render(
      <ConfirmDialog
        open
        onOpenChange={vi.fn()}
        title="Delete the thing?"
        description="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={vi.fn()}
      />,
    );
    // Same Radix focus-guard false positive as FormDialog above.
    expect(await axe(document.body, { rules: { 'aria-hidden-focus': { enabled: false } } })).toHaveNoViolations();
  });

  describe('OrkyoDataTable', () => {
    interface Row {
      id: string;
      name: string;
    }
    const columns: ColumnDef<Row>[] = [{ accessorKey: 'name', header: 'Name' }];
    const data: Row[] = [
      { id: 'r1', name: 'Item 1' },
      { id: 'r2', name: 'Item 2' },
    ];

    it('with data has no detectable a11y violations', async () => {
      const { container } = render(<OrkyoDataTable columns={columns} data={data} />);
      expect(await axe(container)).toHaveNoViolations();
    });

    it('empty (EmptyState) has no detectable a11y violations', async () => {
      const { container } = render(
        <OrkyoDataTable
          columns={columns}
          data={[]}
          emptyMessage="No items yet."
          emptyIcon={<CircleOff className="h-8 w-8" />}
          emptyAction={<Button>Add item</Button>}
        />,
      );
      expect(await axe(container)).toHaveNoViolations();
    });

    it('error (Alert + Try again) has no detectable a11y violations', async () => {
      const { container } = render(
        <OrkyoDataTable columns={columns} data={[]} error="Something went wrong" onRetry={vi.fn()} />,
      );
      expect(await axe(container)).toHaveNoViolations();
    });
  });

  it('EmptyState (with icon + action) has no detectable a11y violations', async () => {
    const { container } = render(
      <EmptyState
        message="Nothing to show."
        icon={<CircleOff className="h-8 w-8" />}
        action={
          <Button>
            <Plus className="h-4 w-4 mr-2" />
            Add item
          </Button>
        }
      />,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it.each(Object.values(REQUEST_STATUS))(
    'RequestStatusBadge (%s) has no detectable a11y violations',
    async (status) => {
      const { container } = render(<RequestStatusBadge status={status} />);
      expect(await axe(container)).toHaveNoViolations();
    },
  );

  it('JobTitleSettings (mocked data) has no detectable a11y violations', async () => {
    const { getJobTitles } = await import('@foundation/src/lib/api/job-titles-api');
    const mockJobTitles: JobTitleInfo[] = [
      {
        id: 'jt-1',
        name: 'Senior Engineer',
        description: 'Leads technical work',
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    ];
    vi.mocked(getJobTitles).mockResolvedValue(mockJobTitles);

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    const { container, getByText } = render(
      <QueryClientProvider client={queryClient}>
        <JobTitleSettings />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(getByText('Senior Engineer')).toBeInTheDocument());
    expect(await axe(container)).toHaveNoViolations();
  });
});

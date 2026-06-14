import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { DialogFormFooter } from './DialogFormFooter';

describe('DialogFormFooter', () => {
  it('renders submit and cancel buttons', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting={false} submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('enables submit when the user can edit', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting={false} submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  it('disables submit for a viewer who cannot edit', () => {
    // useCanEdit is globally mocked to true in src/test/setup.ts; flip it for this case.
    vi.mocked(useCanEdit).mockReturnValueOnce(false);
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting={false} submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('disables submit when submitDisabled is set', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting={false} submitLabel="Save" submitDisabled />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('shows submitting label when submitting', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting submitLabel="Save" submittingLabel="Saving..." />);
    expect(screen.getByRole('button', { name: 'Saving...' })).toBeDisabled();
  });

  it('shows default submitting label when none provided', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Saving...' })).toBeDisabled();
  });

  it('disables cancel button when submitting', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });

  it('calls onCancel when cancel clicked', () => {
    const onCancel = vi.fn();
    render(<DialogFormFooter onCancel={onCancel} isSubmitting={false} submitLabel="Save" />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalled();
  });
});

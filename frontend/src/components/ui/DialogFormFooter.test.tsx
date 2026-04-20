import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DialogFormFooter } from './DialogFormFooter';

describe('DialogFormFooter', () => {
  it('renders submit and cancel buttons', () => {
    render(<DialogFormFooter onCancel={vi.fn()} isSubmitting={false} submitLabel="Save" />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
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

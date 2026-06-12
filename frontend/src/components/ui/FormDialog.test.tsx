import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { FormDialog } from './FormDialog';

function renderDialog(props: Partial<React.ComponentProps<typeof FormDialog>> = {}) {
  const onSubmit = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <FormDialog
      open
      onOpenChange={onOpenChange}
      title="Edit site"
      description="Update the site details"
      onSubmit={onSubmit}
      isSubmitting={false}
      submitLabel="Save"
      {...props}
    >
      <input aria-label="name" />
    </FormDialog>,
  );
  return { onSubmit, onOpenChange };
}

describe('FormDialog', () => {
  it('renders title, description, body, and footer actions', () => {
    renderDialog();
    expect(screen.getByText('Edit site')).toBeInTheDocument();
    expect(screen.getByText('Update the site details')).toBeInTheDocument();
    expect(screen.getByLabelText('name')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('calls onSubmit when the form is submitted', () => {
    const { onSubmit } = renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('closes (onOpenChange false) when Cancel is clicked', () => {
    const { onOpenChange } = renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows the submitting label and disables both actions while submitting', () => {
    renderDialog({ isSubmitting: true, submittingLabel: 'Saving…' });
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });

  it('falls back to a default submitting label', () => {
    renderDialog({ isSubmitting: true });
    expect(screen.getByRole('button', { name: 'Saving...' })).toBeInTheDocument();
  });

  it('disables submit when submitDisabled is set', () => {
    renderDialog({ submitDisabled: true });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('renders an error message when provided', () => {
    renderDialog({ error: 'Something went wrong' });
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });
});

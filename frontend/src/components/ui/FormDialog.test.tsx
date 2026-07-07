import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FormDialog } from './FormDialog';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';

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

  it('disables submit for a viewer who cannot edit (via the shared footer)', () => {
    // useCanEdit is globally mocked to true in src/test/setup.ts; flip it here.
    vi.mocked(useCanEdit).mockReturnValueOnce(false);
    renderDialog();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('submits on Enter from a field (the body is wrapped in a <form>)', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderDialog();
    await user.type(screen.getByLabelText('name'), 'hello{Enter}');
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('does not prompt to discard when closing a clean (non-dirty) form', () => {
    const { onOpenChange } = renderDialog({ dirty: false });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
  });

  it('prompts to discard on close when dirty, and closes only after confirming', async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog({ dirty: true });

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    // Close is intercepted: the guard prompt shows and onOpenChange has NOT fired.
    expect(screen.getByText('Discard changes?')).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);

    await user.click(screen.getByRole('button', { name: /Discard changes/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

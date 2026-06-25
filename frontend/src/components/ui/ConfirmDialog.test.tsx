import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfirmDialog } from './ConfirmDialog';

function renderDialog(props: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
  const onConfirm = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <ConfirmDialog
      open
      onOpenChange={onOpenChange}
      title="Delete the thing?"
      description="This cannot be undone."
      confirmLabel="Delete"
      onConfirm={onConfirm}
      {...props}
    />,
  );
  return { onConfirm, onOpenChange };
}

describe('ConfirmDialog', () => {
  it('renders title, description, and actions with the alertdialog role', () => {
    renderDialog();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText('Delete the thing?')).toBeInTheDocument();
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('calls onConfirm when the confirm button is clicked', async () => {
    const user = userEvent.setup();
    const { onConfirm } = renderDialog();
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('closes (onOpenChange false) when Cancel is clicked', async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('disables both actions while pending', () => {
    renderDialog({ isPending: true });
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });

  describe('confirmPhrase gate', () => {
    it('keeps confirm disabled until the exact phrase is typed', async () => {
      const user = userEvent.setup();
      const { onConfirm } = renderDialog({ confirmPhrase: 'DELETE' });

      const confirm = screen.getByRole('button', { name: 'Delete' });
      expect(confirm).toBeDisabled();

      // Wrong text stays disabled.
      await user.type(screen.getByLabelText(/to confirm/i), 'delete');
      expect(confirm).toBeDisabled();

      // Clear and type the exact phrase.
      await user.clear(screen.getByLabelText(/to confirm/i));
      await user.type(screen.getByLabelText(/to confirm/i), 'DELETE');
      expect(confirm).toBeEnabled();

      await user.click(confirm);
      expect(onConfirm).toHaveBeenCalledTimes(1);
    });
  });
});

import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// Breakpoint is mocked so the phone presentation is deterministic (the real hook
// reads matchMedia). Defaults to desktop; flip per-test.
let mockIsPhone = false;
vi.mock('@foundation/src/hooks/useBreakpoint', () => ({
  useBreakpoint: () => ({
    isPhone: mockIsPhone,
    isTablet: false,
    isDesktop: !mockIsPhone,
    device: mockIsPhone ? 'phone' : 'desktop',
  }),
}));

import { FormDialog } from './FormDialog';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';

afterEach(() => {
  mockIsPhone = false;
});

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

  // ── unsaved-changes guard ─────────────────────────────────────────────────

  it('asks before closing when there are unsaved changes', async () => {
    const { onOpenChange } = renderDialog({ dirty: true });

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.getByText('Discard changes?')).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('lets "Keep editing" return to the form without re-prompting', async () => {
    // Regression: the prompt used to reappear the moment it was dismissed — a trailing
    // outside interaction re-triggered it — so the only way out was to discard the work.
    const { onOpenChange } = renderDialog({ dirty: true });

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click(screen.getByRole('button', { name: 'Keep editing' }));

    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
    // Still editable, and still not closed.
    expect(screen.getByLabelText('name')).toBeInTheDocument();
  });

  it('does not let an outside interaction dismiss a dirty form', async () => {
    const { onOpenChange } = renderDialog({ dirty: true });
    // Radix attaches its outside-pointer listener in a timeout, so fire after a tick or
    // the click lands before anything is listening and the test proves nothing.
    await act(() => new Promise((r) => setTimeout(r, 0)));

    fireEvent.pointerDown(document.body);
    fireEvent.click(document.body);

    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('stays put when a stray outside event trails the "Keep editing" click', async () => {
    // The trap: dismissing the prompt let a trailing pointer/focus-outside event through,
    // which re-opened it. Discarding was then the only way out of the dialog.
    const { onOpenChange } = renderDialog({ dirty: true });
    await act(() => new Promise((r) => setTimeout(r, 0)));

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
    fireEvent.pointerDown(document.body);
    fireEvent.click(document.body);

    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(screen.getByLabelText('name')).toBeInTheDocument();
  });

  it('discards and closes when the person says so', async () => {
    const { onOpenChange } = renderDialog({ dirty: true });

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click(screen.getByRole('button', { name: 'Discard changes' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('closes straight away when nothing has been edited', async () => {
    const { onOpenChange } = renderDialog({ dirty: false });

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByText('Discard changes?')).not.toBeInTheDocument();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('takes the default width token above the phone breakpoint', () => {
    renderDialog();
    expect(screen.getByRole('dialog')).toHaveClass('sm:max-w-[500px]');
  });

  it('takes over the whole screen on a phone instead of floating as a card', () => {
    mockIsPhone = true;
    renderDialog();
    const content = screen.getByRole('dialog');
    // Edge-to-edge and full height — the same presentation ScaffoldDialog uses, so a
    // tall form is not squeezed into a centred band with dead space above and below.
    expect(content).toHaveClass('inset-0', 'h-[100dvh]', 'max-w-none');
    // The width token must not survive alongside it, or the card comes back.
    expect(content).not.toHaveClass('sm:max-w-[500px]');
  });
});

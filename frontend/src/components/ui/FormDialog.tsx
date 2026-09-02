import type { ReactNode } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  ScrollableDialogBody,
  DIALOG_SIZE,
  useFullScreenOnPhone,
  type DialogSize,
} from '@foundation/src/components/ui/dialog';
import { DialogFormFooter } from '@foundation/src/components/ui/DialogFormFooter';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { useDialogDirtyGuard } from '@foundation/src/hooks/useDialogDirtyGuard';
import { cn } from '@foundation/src/lib/utils';

export interface FormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Render the description visually hidden (still announced to screen readers). */
  srOnlyDescription?: boolean;
  /** The form body (inputs, fields). Rendered above the error + footer. */
  children: ReactNode;
  /** Optional error message; passes through to <ErrorAlert />. */
  error?: string | null;
  /** Async-aware submit handler. Loading state cleared automatically. */
  onSubmit: () => void | Promise<void>;
  isSubmitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  /** Disables the submit button when true even if not submitting (form validity). */
  submitDisabled?: boolean;
  /** Shared width token (default "md" = sm:max-w-[500px]). */
  size?: DialogSize;
  /** Tailwind size override for DialogContent; wins over `size` for one-offs. */
  contentClassName?: string;
  /**
   * When provided and true, a close attempt (Cancel / X / ESC / overlay) is
   * intercepted with a discard-changes confirmation. Leave unset for dialogs
   * that don't track dirtiness.
   *
   * Prefer this over wrapping your own `onOpenChange` with `useDialogDirtyGuard`:
   * this dialog also has to stop Radix dismissing itself while the prompt is up,
   * which a caller-side guard cannot do from outside. A caller that self-guards
   * must not also pass `dirty` — that would prompt twice.
   */
  dirty?: boolean;
}

/**
 * Standard "open a dialog → fill a form → submit" scaffold used by foundation
 * and the shells. Owns the dialog shell, error display, and footer; the body
 * is the caller's responsibility.
 */
export function FormDialog({
  open,
  onOpenChange,
  title,
  description,
  srOnlyDescription,
  children,
  error,
  onSubmit,
  isSubmitting,
  submitLabel,
  submittingLabel,
  submitDisabled,
  size = 'md',
  contentClassName,
  dirty,
}: FormDialogProps) {
  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    void onSubmit();
  };

  // Passive when `dirty` is unset/false: guardedOnOpenChange just forwards to
  // onOpenChange, so consumers that don't opt in (or that self-guard) are
  // unaffected. When `dirty` is true, close attempts prompt to discard.
  const { guardedOnOpenChange, confirmOpen, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty: dirty ?? false,
    onOpenChange,
  });

  // Phone: full screen, edge-to-edge — the same presentation ScaffoldDialog uses, so a
  // form dialog and a request dialog behave alike on a phone. Above the phone breakpoint
  // this is undefined and the size token below applies unchanged.
  const phoneClass = useFullScreenOnPhone();

  return (
    <>
    <Dialog open={open} onOpenChange={guardedOnOpenChange}>
      {/* DialogContent is height-bounded + flex-col by default; just collapse the
          default gap so the body's ScrollableDialogBody owns the spacing, and set
          the form width. Tall forms scroll their body with header/footer pinned.

          `overflow-y-hidden` turns OFF the base content's bleed backstop. That backstop is for a
          bare DialogContent, which has no inner scroller; here the ScrollableDialogBody below is
          the scroller, and leaving both on put two scrollbars side by side down the right edge of
          any form tall enough to reach the cap. Nothing is lost by disabling it: the header and
          footer are shrink-0 and the body is flex-1 min-h-0, so the body absorbs every overflow
          the cap creates. (ScaffoldDialog avoids this by fixing h-[85dvh] instead.) */}
      <DialogContent
        className={cn('gap-0 overflow-y-hidden', phoneClass ?? DIALOG_SIZE[size], contentClassName)}
        // While there are unsaved changes, an outside interaction must not dismiss the
        // dialog. Guarding on `dirty` — stable across the whole interaction — rather than
        // only `confirmOpen`, which flips to false the instant "Keep editing" closes the
        // prompt: a trailing pointer or focus-outside event would then re-open the prompt,
        // and the only escape is to discard. Closing stays available through X / Cancel /
        // Escape, which run the prompt exactly once.
        onInteractOutside={(e) => { if (dirty || confirmOpen) e.preventDefault(); }}
        onEscapeKeyDown={(e) => { if (confirmOpen) e.preventDefault(); }}
      >
        <DialogHeader className="shrink-0">
          <DialogTitle>{title}</DialogTitle>
          {description && (
            <DialogDescription className={srOnlyDescription ? 'sr-only' : undefined}>
              {description}
            </DialogDescription>
          )}
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
          <ScrollableDialogBody className="space-y-4 py-4">
            {children}
            <ErrorAlert message={error ?? null} />
          </ScrollableDialogBody>

          {/* Single shared footer: Cancel/Submit + canEdit gating live in DialogFormFooter. */}
          <DialogFormFooter
            className="shrink-0 pt-4"
            onCancel={() => guardedOnOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel={submitLabel}
            submittingLabel={submittingLabel}
            submitDisabled={submitDisabled}
          />
        </form>
      </DialogContent>
    </Dialog>
    {ConfirmDiscardDialog}
    </>
  );
}

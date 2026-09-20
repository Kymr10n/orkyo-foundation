import type * as React from 'react';
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

interface FormDialogBaseProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Render the description visually hidden (still announced to screen readers). */
  srOnlyDescription?: boolean;
  children: ReactNode;
  /** Shared width token. Default "md" with the built-in footer, "lg" without it. */
  size?: DialogSize;
  /** Tailwind size override for DialogContent; wins over `size` for one-offs. */
  contentClassName?: string;
  /** Forwarded to DialogContent (e.g. onOpenAutoFocus). */
  contentProps?: React.ComponentPropsWithoutRef<typeof DialogContent>;
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

/** The standard shape: this dialog owns the form element, the scroller and the footer. */
export interface FormDialogFormProps extends FormDialogBaseProps {
  footer?: undefined;
  /** Optional error message; passes through to <ErrorAlert />. */
  error?: string | null;
  /** Async-aware submit handler. Loading state cleared automatically. */
  onSubmit: () => void | Promise<void>;
  isSubmitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  /** Disables the submit button when true even if not submitting (form validity). */
  submitDisabled?: boolean;
}

/**
 * The scaffold shape: `footer={null}` hands everything below the header to the
 * caller — its own `<form>` / `<Tabs>` / banner / scroll region / footer, spanning
 * the sticky and scrolling regions as one element. Pair with `ScrollableDialogBody`
 * and `DialogFormFooter`.
 */
export interface FormDialogScaffoldProps extends FormDialogBaseProps {
  footer: null;
}

export type FormDialogProps = FormDialogFormProps | FormDialogScaffoldProps;

/**
 * The one dialog shell. Two body shapes, one chrome.
 *
 * By default it is the "open a dialog → fill a form → submit" scaffold: it owns the
 * shell, the scrolling body, the inline error and the Cancel/Submit footer, and the
 * caller supplies only the fields. `footer={null}` switches to the tall variant for
 * bodies too bespoke for that (tabs, banners, multi-region content): fixed height,
 * unpadded content, and nothing below the header but the caller's own markup.
 *
 * Both shapes share the width token, the phone full-screen presentation and the
 * header, which is why they are one component — they drifted apart as two.
 */
export function FormDialog(props: FormDialogProps) {
  const {
    open,
    onOpenChange,
    title,
    description,
    srOnlyDescription,
    children,
    size,
    contentClassName,
    contentProps,
    dirty,
  } = props;

  const ownsFooter = props.footer === undefined;

  // Passive when `dirty` is unset/false: guardedOnOpenChange just forwards to
  // onOpenChange, so consumers that don't opt in (or that self-guard) are
  // unaffected. When `dirty` is true, close attempts prompt to discard.
  const { guardedOnOpenChange, confirmOpen, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty: dirty ?? false,
    onOpenChange,
  });

  // Phone: full screen, edge-to-edge — see useFullScreenOnPhone in dialog.tsx. Above the
  // phone breakpoint this is undefined and the size token applies unchanged.
  const phoneClass = useFullScreenOnPhone();
  const sizeClass = DIALOG_SIZE[size ?? (ownsFooter ? 'md' : 'lg')];

  return (
    <>
      <Dialog open={open} onOpenChange={guardedOnOpenChange}>
        {/* Owned-footer shape: DialogContent is height-bounded + flex-col by default; just
            collapse the default gap so the body's ScrollableDialogBody owns the spacing, and
            set the form width. Tall forms scroll their body with header/footer pinned.

            `overflow-y-hidden` turns OFF the base content's bleed backstop. That backstop is for
            a bare DialogContent, which has no inner scroller; here the ScrollableDialogBody below
            is the scroller, and leaving both on put two scrollbars side by side down the right
            edge of any form tall enough to reach the cap. Nothing is lost by disabling it: the
            header and footer are shrink-0 and the body is flex-1 min-h-0, so the body absorbs
            every overflow the cap creates.

            Scaffold shape: a fixed h-[85dvh] instead, which avoids the same problem, plus p-0 so
            the caller's regions can run edge to edge under the padded header. */}
        <DialogContent
          {...contentProps}
          className={cn(
            ownsFooter
              ? cn('gap-0 overflow-y-hidden', phoneClass ?? sizeClass)
              : cn(phoneClass ?? cn(sizeClass, 'h-[85dvh]'), 'flex flex-col p-0'),
            contentClassName,
          )}
          // While there are unsaved changes, an outside interaction must not dismiss the
          // dialog. Guarding on `dirty` — stable across the whole interaction — rather than
          // only `confirmOpen`, which flips to false the instant "Keep editing" closes the
          // prompt: a trailing pointer or focus-outside event would then re-open the prompt,
          // and the only escape is to discard. Closing stays available through X / Cancel /
          // Escape, which run the prompt exactly once.
          //
          // The caller's own handlers run first: `contentProps` is spread above, so a bare
          // handler here would silently replace a caller's guard instead of adding to it.
          onInteractOutside={(e) => {
            contentProps?.onInteractOutside?.(e);
            if (dirty || confirmOpen) e.preventDefault();
          }}
          onEscapeKeyDown={(e) => {
            contentProps?.onEscapeKeyDown?.(e);
            if (confirmOpen) e.preventDefault();
          }}
        >
          <DialogHeader className={ownsFooter ? 'shrink-0' : 'px-6 pt-6 pb-4 shrink-0'}>
            <DialogTitle>{title}</DialogTitle>
            {description && (
              <DialogDescription className={srOnlyDescription ? 'sr-only' : undefined}>
                {description}
              </DialogDescription>
            )}
          </DialogHeader>

          {props.footer === null ? (
            children
          ) : (
            <FormBody {...props} onCancel={() => guardedOnOpenChange(false)} />
          )}
        </DialogContent>
      </Dialog>
      {ConfirmDiscardDialog}
    </>
  );
}

function FormBody({
  children,
  error,
  onSubmit,
  onCancel,
  isSubmitting,
  submitLabel,
  submittingLabel,
  submitDisabled,
}: FormDialogFormProps & { onCancel: () => void }) {
  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    void onSubmit();
  };

  return (
    <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
      <ScrollableDialogBody className="space-y-4 py-4">
        {children}
        <ErrorAlert message={error ?? null} />
      </ScrollableDialogBody>

      {/* Single shared footer: Cancel/Submit + canEdit gating live in DialogFormFooter. */}
      <DialogFormFooter
        className="shrink-0 pt-4"
        onCancel={onCancel}
        isSubmitting={isSubmitting}
        submitLabel={submitLabel}
        submittingLabel={submittingLabel}
        submitDisabled={submitDisabled}
      />
    </form>
  );
}

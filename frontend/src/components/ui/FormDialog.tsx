import type { ReactNode } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  ScrollableDialogBody,
} from '@foundation/src/components/ui/dialog';
import { DialogFormFooter } from '@foundation/src/components/ui/DialogFormFooter';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { cn } from '@foundation/src/lib/utils';

export interface FormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
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
  /** Tailwind size override for DialogContent (default sm:max-w-[500px]). */
  contentClassName?: string;
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
  children,
  error,
  onSubmit,
  isSubmitting,
  submitLabel,
  submittingLabel,
  submitDisabled,
  contentClassName,
}: FormDialogProps) {
  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    void onSubmit();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* DialogContent is height-bounded + flex-col by default; just collapse the
          default gap so the body's ScrollableDialogBody owns the spacing, and set
          the form width. Tall forms scroll their body with header/footer pinned. */}
      <DialogContent
        className={cn('gap-0 sm:max-w-[500px]', contentClassName)}
      >
        <DialogHeader className="shrink-0">
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
          <ScrollableDialogBody className="space-y-4 py-4">
            {children}
            <ErrorAlert message={error ?? null} />
          </ScrollableDialogBody>

          {/* Single shared footer: Cancel/Submit + canEdit gating live in DialogFormFooter. */}
          <DialogFormFooter
            className="shrink-0 pt-4"
            onCancel={() => onOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel={submitLabel}
            submittingLabel={submittingLabel}
            submitDisabled={submitDisabled}
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}

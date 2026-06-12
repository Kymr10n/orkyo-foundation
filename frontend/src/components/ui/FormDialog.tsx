import type { ReactNode } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  ScrollableDialogBody,
} from '@foundation/src/components/ui/dialog';
import { Button } from '@foundation/src/components/ui/button';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { cn } from '@foundation/src/lib/utils';

export interface FormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
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

          <DialogFooter className="shrink-0 pt-4">
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting || !!submitDisabled}>
              {isSubmitting ? (submittingLabel ?? 'Saving...') : submitLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

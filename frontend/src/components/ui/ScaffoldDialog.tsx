import type * as React from "react";
import type { ReactNode } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DIALOG_SIZE,
  type DialogSize,
} from "@foundation/src/components/ui/dialog";
import { cn } from "@foundation/src/lib/utils";

export interface ScaffoldDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Render the description visually hidden (still announced to screen readers). */
  srOnlyDescription?: boolean;
  /** Shared width token (default "lg" = max-w-2xl). */
  size?: DialogSize;
  /**
   * Everything between the header and the dialog edge: the caller's
   * `<form>` / `<Tabs>` / banner / scroll region / footer. Use
   * `ScrollableDialogBody` for the part that should scroll, and end with a
   * `<Separator />` + footer so header and footer stay pinned.
   */
  children: ReactNode;
  /** Tailwind size override for DialogContent; wins over `size` for one-offs. */
  contentClassName?: string;
  /** Forwarded to DialogContent (e.g. onOpenAutoFocus). */
  contentProps?: React.ComponentPropsWithoutRef<typeof DialogContent>;
}

/**
 * Shared outer chrome for the tall, full-height dialogs whose bodies are too
 * bespoke for `FormDialog` (tabs, banners, multi-region content). It owns only
 * the conflict-free parts — the height-bounded `flex flex-col p-0` container, the
 * sized width, and the padded header — and hands everything below the header to
 * the caller, so a single `<form>`/`<Tabs>` can still span the sticky and
 * scrolling regions. Pair with `ScrollableDialogBody` + `DialogFormFooter`.
 */
export function ScaffoldDialog({
  open,
  onOpenChange,
  title,
  description,
  srOnlyDescription,
  size = "lg",
  children,
  contentClassName,
  contentProps,
}: ScaffoldDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        {...contentProps}
        className={cn(DIALOG_SIZE[size], "h-[85dvh] flex flex-col p-0", contentClassName)}
      >
        <DialogHeader className="px-6 pt-6 pb-4 shrink-0">
          <DialogTitle>{title}</DialogTitle>
          {description && (
            <DialogDescription className={srOnlyDescription ? "sr-only" : undefined}>
              {description}
            </DialogDescription>
          )}
        </DialogHeader>
        {children}
      </DialogContent>
    </Dialog>
  );
}

import type { ReactNode } from "react";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  ScrollableDialogBody,
  DIALOG_SIZE,
  type DialogSize,
} from "@foundation/src/components/ui/dialog";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@foundation/src/components/ui/sheet";
import { cn } from "@foundation/src/lib/utils";

export interface ResponsiveDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Scrollable body content (form fields, details). */
  children: ReactNode;
  /** Pinned footer (e.g. <DialogFormFooter /> or custom actions). */
  footer?: ReactNode;
  /** Shared width token for the desktop/tablet DialogContent (default "md"). */
  size?: DialogSize;
  /** Tailwind size override for the desktop/tablet DialogContent; wins over `size`. */
  contentClassName?: string;
}

/**
 * One dialog API, two presentations: a centered modal `Dialog` on tablet and
 * desktop, and a bottom `Sheet` on phones (thumb-reachable, full-width). Both
 * are height-bounded `flex flex-col` containers, so the body scrolls via the
 * shared `ScrollableDialogBody` while header and footer stay pinned.
 *
 * Use this instead of `Dialog` directly whenever a dialog must be usable on a
 * phone. `FormDialog` remains the scaffold for the common create/edit form
 * case; reach for `ResponsiveDialog` for bespoke bodies or detail views.
 */
export function ResponsiveDialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  size = "md",
  contentClassName,
}: ResponsiveDialogProps) {
  const { isPhone } = useBreakpoint();

  if (isPhone) {
    return (
      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent side="bottom" className={cn("gap-0", contentClassName)}>
          <SheetHeader className="shrink-0">
            <SheetTitle>{title}</SheetTitle>
            {description && <SheetDescription>{description}</SheetDescription>}
          </SheetHeader>
          <ScrollableDialogBody className="space-y-4 py-4">{children}</ScrollableDialogBody>
          {footer && <div className="shrink-0 pt-4">{footer}</div>}
        </SheetContent>
      </Sheet>
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className={cn("gap-0", DIALOG_SIZE[size], contentClassName)}>
        <DialogHeader className="shrink-0">
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        <ScrollableDialogBody className="space-y-4 py-4">{children}</ScrollableDialogBody>
        {footer && <div className="shrink-0 pt-4">{footer}</div>}
      </DialogContent>
    </Dialog>
  );
}

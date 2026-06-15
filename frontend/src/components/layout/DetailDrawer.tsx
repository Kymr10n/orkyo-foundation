import type { ReactNode } from "react";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@foundation/src/components/ui/sheet";
import { ScrollableDialogBody } from "@foundation/src/components/ui/dialog";
import { cn } from "@foundation/src/lib/utils";

export interface DetailDrawerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Scrollable detail content. */
  children: ReactNode;
  /** Optional pinned footer actions. */
  footer?: ReactNode;
  /** Tailwind size override (e.g. a wider drawer on tablet). */
  className?: string;
}

/**
 * A contextual detail panel for touch surfaces: slides in from the right on
 * tablet and up from the bottom on phone. This is the responsive replacement
 * for static side panels (e.g. RequestDetailPanel) on small viewports — on
 * desktop, prefer the existing inline panels and don't reach for this.
 */
export function DetailDrawer({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  className,
}: DetailDrawerProps) {
  const { isPhone } = useBreakpoint();

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={isPhone ? "bottom" : "right"}
        className={cn("gap-0", isPhone ? "max-h-[85dvh]" : "w-full sm:max-w-md", className)}
      >
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

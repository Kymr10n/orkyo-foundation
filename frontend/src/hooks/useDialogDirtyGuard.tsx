import { useCallback, useState } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@foundation/src/components/ui/alert-dialog";

interface UseDialogDirtyGuardOptions {
  /**
   * True when the dialog's form has unsaved changes. The hook only intercepts
   * close attempts (open → false) when this is true.
   */
  isDirty: boolean;
  /**
   * The dialog's original `onOpenChange` handler.
   */
  onOpenChange: (open: boolean) => void;
  /** Optional override for the confirm dialog title. */
  title?: string;
  /** Optional override for the confirm dialog body. */
  description?: string;
}

interface UseDialogDirtyGuardResult {
  /**
   * Wrap your Dialog's `onOpenChange` with this. Closes (open=false) are
   * intercepted with a confirm prompt when `isDirty` is true; opens pass through.
   */
  guardedOnOpenChange: (open: boolean) => void;
  /**
   * Render this element alongside (or inside) your Dialog. It's the confirm
   * prompt that asks the user to discard or keep editing.
   */
  ConfirmDiscardDialog: React.ReactElement;
}

/**
 * Guards a dialog against accidental close when the form has unsaved changes.
 * The caller computes `isDirty` (any signal indicating user edits) and wraps
 * its `onOpenChange`. The hook renders an AlertDialog that asks to confirm
 * discarding.
 *
 * Usage:
 *   const { guardedOnOpenChange, ConfirmDiscardDialog } = useDialogDirtyGuard({
 *     isDirty,
 *     onOpenChange,
 *   });
 *   return (
 *     <>
 *       <Dialog open={open} onOpenChange={guardedOnOpenChange}>...</Dialog>
 *       {ConfirmDiscardDialog}
 *     </>
 *   );
 */
export function useDialogDirtyGuard({
  isDirty,
  onOpenChange,
  title = "Discard changes?",
  description = "You have unsaved changes. They will be lost if you close this dialog.",
}: UseDialogDirtyGuardOptions): UseDialogDirtyGuardResult {
  const [confirmOpen, setConfirmOpen] = useState(false);

  const guardedOnOpenChange = useCallback(
    (open: boolean) => {
      if (!open && isDirty) {
        setConfirmOpen(true);
        return;
      }
      onOpenChange(open);
    },
    [isDirty, onOpenChange],
  );

  const confirmDiscard = useCallback(() => {
    setConfirmOpen(false);
    onOpenChange(false);
  }, [onOpenChange]);

  const ConfirmDiscardDialog = (
    <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep editing</AlertDialogCancel>
          <AlertDialogAction
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            onClick={confirmDiscard}
          >
            Discard changes
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );

  return { guardedOnOpenChange, ConfirmDiscardDialog };
}

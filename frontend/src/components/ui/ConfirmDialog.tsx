import { useEffect, useState, type ReactNode } from "react";
import { Loader2 } from "lucide-react";
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
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { cn } from "@foundation/src/lib/utils";

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Style the confirm action as a destructive (red) button. */
  destructive?: boolean;
  onConfirm: () => void | Promise<void>;
  /** Shows a spinner and disables both actions while true. */
  isPending?: boolean;
  /**
   * Type-to-confirm guard for high-blast-radius actions: when set, the confirm
   * button stays disabled until the user types this exact phrase.
   */
  confirmPhrase?: string;
}

/**
 * Higher-level confirm built on the Radix AlertDialog primitive: proper
 * `alertdialog` role + focus management, a clean prop API, and an optional
 * type-to-confirm gate for dangerous actions.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive,
  onConfirm,
  isPending,
  confirmPhrase,
}: ConfirmDialogProps) {
  const [typed, setTyped] = useState("");

  // Reset the typed phrase whenever the dialog closes so a reopen starts clean.
  useEffect(() => {
    if (!open) setTyped("");
  }, [open]);

  const phraseSatisfied = !confirmPhrase || typed === confirmPhrase;
  const confirmDisabled = !!isPending || !phraseSatisfied;

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>

        {confirmPhrase && (
          <div className="space-y-1.5">
            <Label htmlFor="confirm-phrase">
              Type <span className="font-mono font-semibold">{confirmPhrase}</span> to confirm
            </Label>
            <Input
              id="confirm-phrase"
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
              autoComplete="off"
            />
          </div>
        )}

        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>{cancelLabel}</AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => {
              // The phrase gate also blocks the disabled button, but guard the
              // handler too so a programmatic click can't bypass it.
              if (confirmDisabled) {
                e.preventDefault();
                return;
              }
              void onConfirm();
            }}
            disabled={confirmDisabled}
            className={cn(
              destructive &&
                "bg-destructive text-destructive-foreground hover:bg-destructive/90",
            )}
          >
            {isPending && <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />}
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

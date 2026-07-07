import { Button } from "@foundation/src/components/ui/button";
import { DialogFooter } from "@foundation/src/components/ui/dialog";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";

interface DialogFormFooterProps {
  onCancel: () => void;
  isSubmitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  /** Disables submit even when not submitting and the user can edit (form invalidity). */
  submitDisabled?: boolean;
  className?: string;
}

export function DialogFormFooter({
  onCancel,
  isSubmitting,
  submitLabel,
  submittingLabel,
  submitDisabled,
  className,
}: DialogFormFooterProps) {
  // Viewers get a read-only dialog: the submit is disabled (the backend would 403 anyway).
  // All callers are tenant-content or admin-area dialogs, so canEdit is the right gate for each.
  const canEdit = useCanEdit();
  return (
    <DialogFooter className={className}>
      <Button
        type="button"
        variant="outline"
        onClick={onCancel}
        disabled={isSubmitting}
      >
        Cancel
      </Button>
      <Button
        type="submit"
        loading={isSubmitting}
        disabled={isSubmitting || !canEdit || !!submitDisabled}
      >
        {isSubmitting ? (submittingLabel ?? "Saving...") : submitLabel}
      </Button>
    </DialogFooter>
  );
}

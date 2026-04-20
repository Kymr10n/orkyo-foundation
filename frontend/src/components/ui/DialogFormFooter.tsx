import { Button } from "@/components/ui/button";
import { DialogFooter } from "@/components/ui/dialog";

interface DialogFormFooterProps {
  onCancel: () => void;
  isSubmitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  className?: string;
}

export function DialogFormFooter({
  onCancel,
  isSubmitting,
  submitLabel,
  submittingLabel,
  className,
}: DialogFormFooterProps) {
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
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? (submittingLabel ?? "Saving...") : submitLabel}
      </Button>
    </DialogFooter>
  );
}

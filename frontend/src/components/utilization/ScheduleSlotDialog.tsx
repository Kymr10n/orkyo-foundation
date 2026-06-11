import { useEffect, useState } from "react";
import { format } from "date-fns";
import { Button } from "@foundation/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Label } from "@foundation/src/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { CalendarPlus, Plus } from "lucide-react";
import type { Request } from "@foundation/src/types/requests";

interface ScheduleSlotDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The slot the user selected on the calendar. */
  selection: { start: Date; end: Date } | null;
  /** Unscheduled backlog (drag-to-schedule source). */
  backlog: Request[];
  /** Open the create-new-request flow, prefilled with the selected slot. */
  onCreateNew: () => void;
  /** Schedule an existing unscheduled request into the selected slot. */
  onScheduleExisting: (request: Request) => void;
}

/**
 * Empty-slot chooser shown when the user selects a range on the calendar. Both
 * actions funnel into the existing RequestFormDialog (with the slot prefilled),
 * so all scheduling validation and the space picker are reused — nothing here
 * mutates a request directly.
 */
export function ScheduleSlotDialog({
  open,
  onOpenChange,
  selection,
  backlog,
  onCreateNew,
  onScheduleExisting,
}: ScheduleSlotDialogProps) {
  const [selectedId, setSelectedId] = useState("");

  // Reset the picker each time the dialog opens for a new slot.
  useEffect(() => {
    if (open) setSelectedId("");
  }, [open]);

  const rangeLabel = selection
    ? `${format(selection.start, "EEE d MMM, HH:mm")} – ${format(selection.end, "HH:mm")}`
    : "";

  const handleScheduleExisting = () => {
    const request = backlog.find((r) => r.id === selectedId);
    if (request) onScheduleExisting(request);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Schedule Request</DialogTitle>
          <DialogDescription>{rangeLabel}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="existing-request">Schedule an existing request</Label>
            {backlog.length > 0 ? (
              <div className="flex gap-2">
                <Select value={selectedId} onValueChange={setSelectedId}>
                  <SelectTrigger id="existing-request" className="flex-1">
                    <SelectValue placeholder="Select an unscheduled request" />
                  </SelectTrigger>
                  <SelectContent>
                    {backlog.map((r) => (
                      <SelectItem key={r.id} value={r.id}>
                        {r.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  type="button"
                  variant="secondary"
                  disabled={!selectedId}
                  onClick={handleScheduleExisting}
                >
                  <CalendarPlus className="h-4 w-4" />
                  Schedule
                </Button>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                No unscheduled requests to schedule.
              </p>
            )}
          </div>

          <div className="flex items-center gap-3">
            <Separator className="flex-1" />
            <span className="text-xs text-muted-foreground">or</span>
            <Separator className="flex-1" />
          </div>

          <Button type="button" variant="outline" className="w-full" onClick={onCreateNew}>
            <Plus className="h-4 w-4" />
            Create new request
          </Button>
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

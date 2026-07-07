import { useEffect, useMemo, useState } from "react";
import { addHours, isValid, parse, startOfHour } from "date-fns";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Label } from "@foundation/src/components/ui/label";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";

function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

interface ScheduleToDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The backlog request being scheduled. */
  request: Request | null;
  /** Spaces the grid currently shows (reuse of the page's spaces query). */
  spaces: Space[];
  /**
   * Persist the choice. Given the picked space's resourceId and the start time,
   * the caller reuses the drag path's schedule handler (duration → endTs,
   * mutation, conflict feedback). Rejects on failure so the dialog stays open.
   */
  onSchedule: (resourceId: string, startTs: Date) => Promise<void>;
  isSubmitting?: boolean;
  /** Prefill for the start field. Defaults to the next full hour. */
  defaultStart?: Date;
}

/**
 * Keyboard/pointer-agnostic alternative to dragging a backlog card onto the
 * grid: pick a space + start time, then schedule via the same mutation the drag
 * path uses. The end time is derived by the caller from the request's duration
 * (identical to `handleScheduleToGrid`), so validation/conflict behaviour matches.
 */
export function ScheduleToDialog({
  open,
  onOpenChange,
  request,
  spaces,
  onSchedule,
  isSubmitting = false,
  defaultStart,
}: ScheduleToDialogProps) {
  const [spaceId, setSpaceId] = useState("");
  const [startValue, setStartValue] = useState("");

  const initialStart = useMemo(
    () => defaultStart ?? startOfHour(addHours(new Date(), 1)),
    [defaultStart],
  );

  // Reset the picker each time the dialog opens for a new request.
  useEffect(() => {
    if (open) {
      setSpaceId("");
      setStartValue(toLocalInput(initialStart));
    }
  }, [open, initialStart]);

  const parsedStart = startValue
    ? parse(startValue, "yyyy-MM-dd'T'HH:mm", new Date())
    : null;
  const startValid = !!parsedStart && isValid(parsedStart);

  const handleSubmit = async () => {
    if (!spaceId || !parsedStart || !startValid) return;
    try {
      await onSchedule(spaceId, parsedStart);
      onOpenChange(false);
    } catch {
      // The schedule mutation surfaces its own error toast; keep the dialog
      // open so the user can adjust and retry.
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={request ? `Schedule "${request.name}"` : "Schedule request"}
      description="Pick a space and a start time. The end time is derived from the request's duration."
      size="sm"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Schedule"
      submittingLabel="Scheduling…"
      submitDisabled={!spaceId || !startValid}
    >
      <div className="space-y-2">
        <Label htmlFor="schedule-to-space">Space</Label>
        <Select value={spaceId} onValueChange={setSpaceId}>
          <SelectTrigger id="schedule-to-space">
            <SelectValue placeholder="Select a space" />
          </SelectTrigger>
          <SelectContent>
            {spaces.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="schedule-to-start">Start</Label>
        <DateTimePicker id="schedule-to-start" value={startValue} onChange={setStartValue} />
      </div>
    </FormDialog>
  );
}

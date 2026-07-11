import { Label } from "@foundation/src/components/ui/label";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { combineDateTimeFields, splitDateTimeFields } from "@foundation/src/lib/utils/picker-utils";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";
import { Calendar } from "lucide-react";

interface RequestScheduleSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  /** View mode: disable the pickers (values still shown). */
  readOnly?: boolean;
}

export function RequestScheduleSection({
  state,
  setField,
  readOnly = false,
}: RequestScheduleSectionProps) {
  return (
    <div>
      <h4 className="text-sm font-medium flex items-center gap-2">
        <Calendar className="h-4 w-4" />
        Schedule (Optional)
      </h4>
      <div className="space-y-4 pt-4">
        <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Start</Label>
              <DateTimePicker
                id="startDateTime"
                value={combineDateTimeFields(state.startDate, state.startTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('startDate', d), (t) => setField('startTime', t))}
                placeholder="Pick start date & time"
                disabled={readOnly}
              />
            </div>

            <div className="space-y-2">
              <Label>End</Label>
              <DateTimePicker
                id="endDateTime"
                value={combineDateTimeFields(state.endDate, state.endTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('endDate', d), (t) => setField('endTime', t))}
                placeholder="Pick end date & time"
                disabled={readOnly}
              />
            </div>
          </div>

          <p className="text-xs text-muted-foreground">
            Leave dates blank to create an unscheduled request. Schedule later from the utilization view.
          </p>
        </div>
    </div>
  );
}

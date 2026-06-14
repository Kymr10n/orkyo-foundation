import { Label } from "@foundation/src/components/ui/label";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { combineDateTimeFields, splitDateTimeFields } from "@foundation/src/lib/utils/picker-utils";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";

interface RequestConstraintsSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
}

export function RequestConstraintsSection({
  state,
  setField,
}: RequestConstraintsSectionProps) {
  return (
    <div>
      <h4 className="text-sm font-medium">Scheduling Constraints (Optional)</h4>
      <div className="space-y-4 pt-4">
        <p className="text-xs text-muted-foreground">
          Specify time windows when this request can be scheduled.
        </p>

        <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label className="text-xs">Earliest Start</Label>
              <DateTimePicker
                id="earliestStart"
                value={combineDateTimeFields(state.earliestStartDate, state.earliestStartTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('earliestStartDate', d), (t) => setField('earliestStartTime', t))}
                placeholder="Pick earliest start"
              />
            </div>

            <div className="space-y-2">
              <Label className="text-xs">Latest End</Label>
              <DateTimePicker
                id="latestEnd"
                value={combineDateTimeFields(state.latestEndDate, state.latestEndTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('latestEndDate', d), (t) => setField('latestEndTime', t))}
                placeholder="Pick latest end"
              />
            </div>
          </div>
      </div>
    </div>
  );
}

import { Label } from "@foundation/src/components/ui/label";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { combineDateTimeFields, splitDateTimeFields } from "@foundation/src/lib/utils/picker-utils";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";

interface RequestConstraintsSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  /** View mode: disable the pickers (values still shown). */
  readOnly?: boolean;
  /** Section heading. Defaults to the leaf-request wording. */
  title?: string;
  /** Sub-copy under the heading. Defaults to the leaf-request wording. */
  description?: string;
}

export function RequestConstraintsSection({
  state,
  setField,
  readOnly = false,
  title = "Scheduling Constraints (Optional)",
  description = "Specify time windows when this request can be scheduled.",
}: RequestConstraintsSectionProps) {
  return (
    <div>
      <h4 className="text-sm font-medium">{title}</h4>
      <div className="space-y-4 pt-4">
        <p className="text-xs text-muted-foreground">
          {description}
        </p>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label className="text-xs">Earliest Start</Label>
              <DateTimePicker
                id="earliestStart"
                value={combineDateTimeFields(state.earliestStartDate, state.earliestStartTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('earliestStartDate', d), (t) => setField('earliestStartTime', t))}
                placeholder="Pick earliest start"
                disabled={readOnly}
              />
            </div>

            <div className="space-y-2">
              <Label className="text-xs">Latest End</Label>
              <DateTimePicker
                id="latestEnd"
                value={combineDateTimeFields(state.latestEndDate, state.latestEndTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('latestEndDate', d), (t) => setField('latestEndTime', t))}
                placeholder="Pick latest end"
                disabled={readOnly}
              />
            </div>
          </div>
      </div>
    </div>
  );
}

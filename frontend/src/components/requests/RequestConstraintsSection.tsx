import { Label } from "@/components/ui/label";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { combineDateTimeFields, splitDateTimeFields } from "@/lib/utils/picker-utils";
import type { useRequestForm } from "@/hooks/useRequestForm";
import { ChevronDown } from "lucide-react";

interface RequestConstraintsSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  toggleSection: ReturnType<typeof useRequestForm>['toggleSection'];
}

export function RequestConstraintsSection({
  state,
  setField,
  toggleSection,
}: RequestConstraintsSectionProps) {
  return (
    <Collapsible open={state.openSections.constraints} onOpenChange={() => toggleSection('constraints')}>
      <CollapsibleTrigger className="flex items-center justify-between w-full p-3 hover:bg-muted/50 rounded-lg">
        <h3 className="text-sm font-medium">Scheduling Constraints (Optional)</h3>
        <ChevronDown className={`h-4 w-4 transition-transform ${state.openSections.constraints ? 'rotate-180' : ''}`} />
      </CollapsibleTrigger>
      <CollapsibleContent className="pt-4">
        <div className="space-y-4">
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
      </CollapsibleContent>
    </Collapsible>
  );
}

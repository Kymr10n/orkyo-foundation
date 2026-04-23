import { Label } from "@foundation/src/components/ui/label";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@foundation/src/components/ui/collapsible";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { SPACE_NONE_PLACEHOLDER } from "@foundation/src/constants";
import { combineDateTimeFields, splitDateTimeFields } from "@foundation/src/lib/utils/picker-utils";
import type { Space } from "@foundation/src/types/space";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";
import { Calendar, ChevronDown, MapPin } from "lucide-react";

interface RequestScheduleSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  toggleSection: ReturnType<typeof useRequestForm>['toggleSection'];
  availableSpaces: Space[];
}

export function RequestScheduleSection({
  state,
  setField,
  toggleSection,
  availableSpaces,
}: RequestScheduleSectionProps) {
  return (
    <Collapsible open={state.openSections.schedule} onOpenChange={() => toggleSection('schedule')}>
      <CollapsibleTrigger className="flex items-center justify-between w-full p-3 hover:bg-muted/50 rounded-lg">
        <h3 className="text-sm font-medium flex items-center gap-2">
          <Calendar className="h-4 w-4" />
          Schedule (Optional)
        </h3>
        <ChevronDown className={`h-4 w-4 transition-transform ${state.openSections.schedule ? 'rotate-180' : ''}`} />
      </CollapsibleTrigger>
      <CollapsibleContent className="pt-4">
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Start</Label>
              <DateTimePicker
                id="startDateTime"
                value={combineDateTimeFields(state.startDate, state.startTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('startDate', d), (t) => setField('startTime', t))}
                placeholder="Pick start date & time"
              />
            </div>

            <div className="space-y-2">
              <Label>End</Label>
              <DateTimePicker
                id="endDateTime"
                value={combineDateTimeFields(state.endDate, state.endTime)}
                onChange={(v) => splitDateTimeFields(v, (d) => setField('endDate', d), (t) => setField('endTime', t))}
                placeholder="Pick end date & time"
              />
            </div>
          </div>

          {/* Space Assignment */}
          <div className="space-y-2">
            <Label htmlFor="spaceId" className="flex items-center gap-2">
              <MapPin className="h-4 w-4" />
              Space
            </Label>
            <Select value={state.selectedSpaceId || SPACE_NONE_PLACEHOLDER} onValueChange={(value) => setField('selectedSpaceId', value === SPACE_NONE_PLACEHOLDER ? "" : value)}>
              <SelectTrigger id="spaceId">
                <SelectValue placeholder="No space assigned (unscheduled)" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SPACE_NONE_PLACEHOLDER}>
                  <span className="text-muted-foreground">No space (unscheduled)</span>
                </SelectItem>
                {availableSpaces.map((space) => (
                  <SelectItem key={space.id} value={space.id}>
                    {space.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <p className="text-xs text-muted-foreground">
            Leave dates blank to create an unscheduled request. Schedule later from the utilization view.
          </p>
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

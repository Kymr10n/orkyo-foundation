import { TabsContent } from "@foundation/src/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { SPACE_NONE_PLACEHOLDER } from "@foundation/src/constants";
import { combineDateTimeToISO } from "@foundation/src/lib/utils";
import { ConflictIndicator } from "./ConflictIndicator";
import { RequestPeopleSection } from "./RequestPeopleSection";
import type { Conflict } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";
import { MapPin } from "lucide-react";

interface RequestResourcesSectionProps {
  activeTab: string;
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  spaceConflicts: Conflict[];
  availableSpaces: Space[];
  readOnly: boolean;
  requestId: string | undefined;
  hasEditableSchedule: boolean;
  onBlockersChange: (hasBlockers: boolean) => void;
  conflictsByResourceId: Map<string, Conflict[]>;
}

/**
 * RESOURCES tab — leaf only. forceMount + conditional hidden keeps pending rows and
 * blocker state alive across tab switches (Radix would otherwise unmount it).
 */
export function RequestResourcesSection({
  activeTab,
  state,
  setField,
  spaceConflicts,
  availableSpaces,
  readOnly,
  requestId,
  hasEditableSchedule,
  onBlockersChange,
  conflictsByResourceId,
}: RequestResourcesSectionProps) {
  return (
    <TabsContent
      value="resources"
      forceMount
      className={activeTab === 'resources' ? 'mt-0 space-y-6' : 'mt-0 hidden'}
    >
      {/* Space */}
      <div>
        <h4 className="text-sm font-medium flex items-center gap-2">
          <MapPin className="h-4 w-4" />
          Space
          <ConflictIndicator conflicts={spaceConflicts} />
        </h4>
        <div className="space-y-2 pt-4">
          <Select
            value={state.selectedResourceId || SPACE_NONE_PLACEHOLDER}
            onValueChange={(value) => setField('selectedResourceId', value === SPACE_NONE_PLACEHOLDER ? "" : value)}
            disabled={readOnly}
          >
            <SelectTrigger id="resourceId">
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
      </div>

      <Separator />

      {/* People */}
      <RequestPeopleSection
        requestId={requestId}
        requestStartTs={
          hasEditableSchedule && state.startDate && state.startTime
            ? (() => { try { return combineDateTimeToISO(state.startDate, state.startTime); } catch { return undefined; } })()
            : undefined
        }
        requestEndTs={
          hasEditableSchedule && state.endDate && state.endTime
            ? (() => { try { return combineDateTimeToISO(state.endDate, state.endTime); } catch { return undefined; } })()
            : undefined
        }
        onBlockersChange={onBlockersChange}
        conflictsByResourceId={conflictsByResourceId}
        readOnly={readOnly}
      />
    </TabsContent>
  );
}

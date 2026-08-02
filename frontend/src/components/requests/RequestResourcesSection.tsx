import { TabsContent } from "@foundation/src/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { RESOURCE_NONE_PLACEHOLDER } from "@foundation/src/constants";
import { combineDateTimeToISO } from "@foundation/src/lib/utils";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { resourceTypeIcon } from "@foundation/src/components/resources/resource-type-icon";
import { useQuery } from "@tanstack/react-query";
import { ConflictIndicator } from "./ConflictIndicator";
import { RequestPeopleSection } from "./RequestPeopleSection";
import { RequestTargetTypesField } from "./RequestTargetTypesField";
import type { Conflict } from "@foundation/src/types/requests";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";

interface RequestResourcesSectionProps {
  activeTab: string;
  state: ReturnType<typeof useRequestForm>['state'];
  setField: ReturnType<typeof useRequestForm>['setField'];
  readOnly: boolean;
  requestId: string | undefined;
  /** Site the request is scoped to; narrows every picker to resources available there. */
  siteId: string;
  hasEditableSchedule: boolean;
  onBlockersChange: (hasBlockers: boolean) => void;
  conflictsByResourceId: Map<string, Conflict[]>;
}

/**
 * One resource picker for one targeted type.
 *
 * A child component rather than a loop of hooks in the parent: how many types a request
 * targets is runtime data and can change between renders, which the rules of hooks forbid.
 * Giving each type its own component instance gives each its own stable hook call.
 */
function ResourceTypePicker({
  type,
  siteId,
  selectedResourceId,
  onSelect,
  conflicts,
  readOnly,
}: {
  type: ResourceTypeInfo;
  siteId: string;
  selectedResourceId: string;
  onSelect: (resourceId: string) => void;
  conflicts: Conflict[];
  readOnly: boolean;
}) {
  const Icon = resourceTypeIcon(type.icon);
  const { data: resources } = useQuery({
    queryKey: [...qk.resources.byType(type.key), { siteId: siteId || null }],
    queryFn: () => getResources({ resourceTypeKey: type.key, isActive: true, siteId: siteId || undefined }),
  });

  return (
    <div>
      <h4 className="text-sm font-medium flex items-center gap-2">
        <Icon className="h-4 w-4" />
        {type.displayName}
        <ConflictIndicator conflicts={conflicts} />
      </h4>
      <div className="space-y-2 pt-4">
        <Select
          value={selectedResourceId || RESOURCE_NONE_PLACEHOLDER}
          onValueChange={(value) => onSelect(value === RESOURCE_NONE_PLACEHOLDER ? "" : value)}
          disabled={readOnly}
        >
          <SelectTrigger id={`resource-${type.key}`} aria-label={type.displayName}>
            <SelectValue placeholder={`No ${type.displayName.toLowerCase()} assigned (unscheduled)`} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={RESOURCE_NONE_PLACEHOLDER}>
              <span className="text-muted-foreground">No {type.displayName.toLowerCase()} (unscheduled)</span>
            </SelectItem>
            {(resources?.data ?? []).map((resource) => (
              <SelectItem key={resource.id} value={resource.id}>
                {resource.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}

/**
 * RESOURCES tab — leaf only. forceMount + conditional hidden keeps pending rows and
 * blocker state alive across tab switches (Radix would otherwise unmount it).
 */
export function RequestResourcesSection({
  activeTab,
  state,
  setField,
  readOnly,
  requestId,
  siteId,
  hasEditableSchedule,
  onBlockersChange,
  conflictsByResourceId,
}: RequestResourcesSectionProps) {
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const typesByKey = new Map(resourceTypes.map((t) => [t.key, t]));

  return (
    <TabsContent
      value="resources"
      forceMount
      className={activeTab === 'resources' ? 'mt-0 space-y-6' : 'mt-0 hidden'}
    >
      <RequestTargetTypesField
        resourceTypes={resourceTypes}
        selectedKeys={state.targetResourceTypeKeys}
        onChange={(keys) => setField('targetResourceTypeKeys', keys)}
        readOnly={readOnly}
      />

      <Separator />

      {/* One picker per targeted type. A type the tenant has since deactivated has no
          entry here, so it renders nothing rather than an unlabelled picker. */}
      {state.targetResourceTypeKeys.map((key) => {
        const type = typesByKey.get(key);
        if (!type) return null;
        const selectedResourceId = state.selectedResourceIds[key] ?? '';
        return (
          <ResourceTypePicker
            key={key}
            type={type}
            siteId={siteId}
            selectedResourceId={selectedResourceId}
            onSelect={(resourceId) =>
              setField('selectedResourceIds', { ...state.selectedResourceIds, [key]: resourceId })
            }
            conflicts={selectedResourceId ? conflictsByResourceId.get(selectedResourceId) ?? [] : []}
            readOnly={readOnly}
          />
        );
      })}

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

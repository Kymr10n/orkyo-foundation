import { TabsContent } from "@foundation/src/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { RESOURCE_NONE_PLACEHOLDER } from "@foundation/src/constants";
import { combineDateTimeToISO } from "@foundation/src/lib/utils";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { getUtilizationByResource, type ResourceUtilizationBucket } from "@foundation/src/lib/api/resource-utilization-api";
import { Badge } from "@foundation/src/components/ui/badge";
import { useDebouncedCallback } from "@foundation/src/hooks/useDebouncedCallback";
import { useEffect, useState } from "react";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { resourceTypeIcon } from "@foundation/src/components/resources/resource-type-icon";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
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
 * Whether a resource can take the request's window.
 *
 * The picker used to list bare names, so choosing a machine meant guessing, saving, and
 * reading the conflict afterwards. These four states answer the question in the dropdown.
 */
type ResourceAvailability = "free" | "partial" | "busy" | "unavailable";

const AVAILABILITY_LABEL: Record<ResourceAvailability, string> = {
  free: "Free",
  partial: "Partly booked",
  busy: "Busy",
  unavailable: "Unavailable",
};

/**
 * Reduce a resource's buckets over the window to one answer.
 *
 * Reads booleans and thresholds rather than the exact percentages, so it stays correct
 * whatever the capacity denominator is: "is anything already on this machine" does not
 * depend on how the utilization percentage is scaled.
 */
function deriveAvailability(buckets: ResourceUtilizationBucket[]): ResourceAvailability | undefined {
  if (buckets.length === 0) return undefined;
  // No bookable time at all: an absence, a site closure, or entirely outside working hours.
  if (buckets.every((b) => b.effectiveAvailabilityPercent === 0)) return "unavailable";
  if (buckets.some((b) => b.isExclusiveOccupied || b.allocatedPercent >= 100)) return "busy";
  if (buckets.some((b) => b.allocatedPercent > 0)) return "partial";
  return "free";
}

function AvailabilityBadge({ availability }: { availability: ResourceAvailability }) {
  const tone =
    availability === "free" ? "border-transparent bg-emerald-100 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200"
    : availability === "partial" ? "border-transparent bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200"
    : availability === "busy" ? "border-transparent bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-200"
    : "border-transparent bg-muted text-muted-foreground";
  return (
    <Badge variant="outline" className={`ml-2 shrink-0 text-[10px] font-medium ${tone}`}>
      {AVAILABILITY_LABEL[availability]}
    </Badge>
  );
}

/**
 * Hold a value back until it stops changing, so editing a time field does not fire a
 * query per keystroke. 400 ms matches the People section's validation debounce.
 */
function useDebouncedValue<T>(value: T, delay: number): T {
  const [settled, setSettled] = useState(value);
  const commit = useDebouncedCallback((next: T) => setSettled(next), delay);
  useEffect(() => { commit(value); }, [value, commit]);
  return settled;
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
  windowStartTs,
  windowEndTs,
}: {
  type: ResourceTypeInfo;
  siteId: string;
  selectedResourceId: string;
  onSelect: (resourceId: string) => void;
  conflicts: Conflict[];
  readOnly: boolean;
  /** The request's window, when it has a complete one. Drives the availability badges. */
  windowStartTs?: string;
  windowEndTs?: string;
}) {
  const Icon = resourceTypeIcon(type.icon);
  const { data: resources, isPending } = useQuery({
    queryKey: [...qk.resources.byType(type.key), { siteId: siteId || null }],
    queryFn: () => getResources({ resourceTypeKey: type.key, isActive: true, siteId: siteId || undefined }),
  });

  const settledStart = useDebouncedValue(windowStartTs, 400);
  const settledEnd = useDebouncedValue(windowEndTs, 400);
  const from = settledStart ? new Date(settledStart) : undefined;
  const to = settledEnd ? new Date(settledEnd) : undefined;
  const hasWindow = !!from && !!to && from < to;
  // Hourly resolution below a fortnight, so a job earlier the same day does not make the
  // whole day read as Busy; daily above it, to keep the payload bounded.
  const granularity =
    hasWindow && to.getTime() - from.getTime() > 14 * 24 * 60 * 60 * 1000 ? "day" : "hour";

  const { data: utilization } = useQuery({
    // The key is built only when there is a window: the factory stamps the dates with
    // toISOString(), which throws on undefined, and `enabled` gates the fetch but not
    // the key — a request with no schedule would take the whole tab down with it.
    queryKey: hasWindow
      ? qk.utilization.byResource(type.key, siteId || null, from, to, granularity)
      : [...qk.utilization.byResourceAll(), "no-window", type.key],
    queryFn: () => getUtilizationByResource(from!, to!, granularity, type.key, siteId || undefined),
    enabled: hasWindow,
    // The badges are a hint, not a gate: keep the previous answer on screen while the
    // next one loads rather than flickering the whole list empty on every window edit.
    placeholderData: keepPreviousData,
  });

  const availabilityByResource = new Map<string, ResourceAvailability>();
  for (const entry of utilization ?? []) {
    const availability = deriveAvailability(entry.buckets);
    if (availability) availabilityByResource.set(entry.resourceId, availability);
  }

  const options = resources?.data ?? [];
  // A dropdown holding only "none" is indistinguishable from one where the user chose none.
  // Say why it is empty instead — and because the query is site-scoped, "none here" and "none
  // at all" are different problems with different fixes.
  const isEmpty = !isPending && options.length === 0;
  const label = type.displayName.toLowerCase();

  return (
    <div>
      <h4 className="text-sm font-medium flex items-center gap-2">
        {/* Stable reference from a module-level icon registry, not built in render. */}
        {/* eslint-disable-next-line react-hooks/static-components */}
        <Icon className="h-4 w-4" />
        {type.displayName}
        <ConflictIndicator conflicts={conflicts} />
      </h4>
      <div className="space-y-2 pt-4">
        <Select
          value={selectedResourceId || RESOURCE_NONE_PLACEHOLDER}
          onValueChange={(value) => onSelect(value === RESOURCE_NONE_PLACEHOLDER ? "" : value)}
          disabled={readOnly || isEmpty}
        >
          <SelectTrigger id={`resource-${type.key}`} aria-label={type.displayName}>
            <SelectValue placeholder={`No ${type.displayName.toLowerCase()} assigned (unscheduled)`} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={RESOURCE_NONE_PLACEHOLDER}>
              <span className="text-muted-foreground">No {type.displayName.toLowerCase()} (unscheduled)</span>
            </SelectItem>
            {options.map((resource) => {
              const availability = availabilityByResource.get(resource.id);
              return (
                <SelectItem key={resource.id} value={resource.id}>
                  <span className="flex w-full items-center justify-between">
                    <span className="truncate">{resource.name}</span>
                    {availability && <AvailabilityBadge availability={availability} />}
                  </span>
                </SelectItem>
              );
            })}
          </SelectContent>
        </Select>
        {isEmpty && (
          <p className="text-sm text-muted-foreground">
            {siteId
              ? `No active ${label} at this site. Add one, or move an existing ${label} here.`
              : `No active ${label} yet. Add one before this request can be scheduled.`}
          </p>
        )}
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

  // The request's window, computed once for both the pickers (availability badges) and the
  // People section (assignment validation). Undefined until both halves parse — a partially
  // typed time is not a window, and combineDateTimeToISO throws on one.
  const toIso = (date: string, time: string) => {
    if (!hasEditableSchedule || !date || !time) return undefined;
    try { return combineDateTimeToISO(date, time); } catch { return undefined; }
  };
  const windowStartTs = toIso(state.startDate, state.startTime);
  const windowEndTs = toIso(state.endDate, state.endTime);

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

      {/* One picker per targeted type. A type the tenant has since deactivated has no entry
          here, so it renders nothing rather than an unlabelled picker. Directory types are
          skipped for the same reason they cannot be targeted: a single-slot picker would
          cancel the rest of the crew the People section below manages. */}
      {state.targetResourceTypeKeys.map((key) => {
        const type = typesByKey.get(key);
        if (!type || type.hasDirectoryProfile) return null;
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
            windowStartTs={windowStartTs}
            windowEndTs={windowEndTs}
          />
        );
      })}

      <Separator />

      {/* People */}
      <RequestPeopleSection
        requestId={requestId}
        requestStartTs={windowStartTs}
        requestEndTs={windowEndTs}
        onBlockersChange={onBlockersChange}
        conflictsByResourceId={conflictsByResourceId}
        readOnly={readOnly}
      />
    </TabsContent>
  );
}

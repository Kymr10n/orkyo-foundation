import { useMemo, useState } from "react";
import { cn } from "@foundation/src/lib/utils";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Switch } from "@foundation/src/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { Badge } from "@foundation/src/components/ui/badge";
import { Combobox, type ComboboxOption } from "@foundation/src/components/ui/combobox";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { toDateTimeLocalValue } from "@foundation/src/lib/formatters";
import { Plus, X } from "lucide-react";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import {
  useAddAvailabilityEventScope,
  useDeleteAvailabilityEventScope,
  useScopePickerOptions,
  type ScopeDraft,
} from "@foundation/src/hooks/useAvailabilityEvents";
import {
  type AvailabilityEventInfo,
  type AvailabilityEventScopeInfo,
  type AvailabilityEventType,
  type DefaultEffect,
  type ScopeEffect,
  type ScopeTargetType,
  type CreateAvailabilityEventRequest,
  type UpdateAvailabilityEventRequest,
} from "@foundation/src/lib/api/availability-events-api";
import { useEntityFormDialog } from "@foundation/src/hooks/useEntityFormDialog";
import { useSaveAvailabilityEvent } from "@foundation/src/hooks/useScheduling";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  siteId: string;
  event: AvailabilityEventInfo | null;
}

const EVENT_TYPES: { value: AvailabilityEventType; label: string }[] = [
  { value: "public_holiday", label: "Public holiday" },
  { value: "shutdown", label: "Shutdown" },
  { value: "maintenance", label: "Maintenance" },
  { value: "custom", label: "Custom" },
];

const DEFAULT_EFFECTS: { value: DefaultEffect; label: string }[] = [
  { value: "closed", label: "Closed" },
  { value: "available", label: "Available" },
];

const SCOPE_EFFECTS: { value: ScopeEffect; label: string }[] = [
  { value: "available", label: "Available" },
  { value: "closed", label: "Closed" },
];

const TARGET_TYPES: { value: ScopeTargetType; label: string }[] = [
  { value: "resource", label: "Resource" },
  { value: "resource_group", label: "Resource group" },
  { value: "resource_type", label: "Resource type" },
];

// Sentinel used by the type-filter Select to represent "no filter" — Radix
// Select does not allow an empty string as an item value.
const ALL_TYPES_VALUE = "__all__";

// ── Scope row (controlled) ──────────────────────────────────────────────────

function ScopeRow({
  scope,
  onDelete,
  isDeleting,
}: {
  scope: ScopeDraft;
  onDelete: () => void;
  isDeleting?: boolean;
}) {
  const { resources, groups, resourceTypes } = useScopePickerOptions();

  const targetLabel = () => {
    if (scope.targetType === "resource") {
      return resources?.find((r) => r.id === scope.targetId)?.name ?? scope.targetId.slice(0, 8);
    }
    if (scope.targetType === "resource_group") {
      return groups?.find((g) => g.id === scope.targetId)?.name ?? scope.targetId.slice(0, 8);
    }
    if (scope.targetType === "resource_type") {
      return resourceTypes?.find((t) => t.id === scope.targetId)?.displayName ?? scope.targetId.slice(0, 8);
    }
    return scope.targetId.slice(0, 8);
  };

  const targetTypeBadge: Record<ScopeTargetType, string> = {
    resource: "Resource",
    resource_group: "Group",
    resource_type: "Type",
  };

  return (
    <div className="flex items-center gap-2 py-1.5 text-sm">
      <Badge variant="outline" className="text-xs shrink-0">
        {targetTypeBadge[scope.targetType]}
      </Badge>
      <span className="flex-1 truncate">{targetLabel()}</span>
      <span className="text-muted-foreground shrink-0">→</span>
      <Badge
        variant={scope.effect === "available" ? "secondary" : "destructive"}
        className="text-xs shrink-0"
      >
        {scope.effect}
      </Badge>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6 shrink-0"
        onClick={onDelete}
        loading={isDeleting}
        disabled={isDeleting}
        aria-label="Remove override"
      >
        {!isDeleting && <X className="h-3 w-3" />}
      </Button>
    </div>
  );
}

// Edit-mode wrapper: handles the delete mutation against the server.
function ServerScopeRow({
  siteId,
  eventId,
  scope,
}: {
  siteId: string;
  eventId: string;
  scope: AvailabilityEventScopeInfo;
}) {
  const deleteScope = useDeleteAvailabilityEventScope(siteId, eventId, scope.id);

  return (
    <ScopeRow
      scope={scope}
      onDelete={() => deleteScope.mutate()}
      isDeleting={deleteScope.isPending}
    />
  );
}

// ── Add scope inline form (controlled) ──────────────────────────────────────

function AddScopeForm({
  onAdd,
  isSubmitting,
  error,
}: {
  onAdd: (req: ScopeDraft) => void | Promise<void>;
  isSubmitting?: boolean;
  error?: string | null;
}) {
  const canEdit = useCanEdit();
  const [targetType, setTargetType] = useState<ScopeTargetType>("resource");
  const [targetId, setTargetId] = useState("");
  const [effect, setEffect] = useState<ScopeEffect>("available");
  // Only meaningful for `resource` / `resource_group` targets. Empty = no filter.
  const [filterTypeKey, setFilterTypeKey] = useState<string>("");

  const { resources, groups, resourceTypes } = useScopePickerOptions();

  const handleAdd = async () => {
    if (!targetId) return;
    await onAdd({ targetType, targetId, effect });
    // Clear selection regardless of caller's success/failure handling — the
    // caller is responsible for surfacing errors via the `error` prop. Keep
    // the type filter so the user can quickly add another override of the
    // same kind.
    setTargetId("");
  };

  const typeFilterOptions: ComboboxOption[] = useMemo(
    () =>
      (resourceTypes ?? [])
        .filter((t) => t.isActive)
        .map((t) => ({ id: t.key, label: t.displayName })),
    [resourceTypes],
  );

  const targetOptions: ComboboxOption[] = useMemo(() => {
    if (targetType === "resource") {
      return (resources ?? [])
        .filter((r) => !filterTypeKey || r.resourceTypeKey === filterTypeKey)
        .map((r) => ({ id: r.id, label: `${r.name} (${r.resourceTypeKey})` }));
    }
    if (targetType === "resource_group") {
      return (groups ?? [])
        .filter((g) => !filterTypeKey || g.resourceTypeKey === filterTypeKey)
        .map((g) => ({ id: g.id, label: `${g.name} (${g.resourceTypeKey})` }));
    }
    if (targetType === "resource_type") {
      return (resourceTypes ?? [])
        .filter((t) => t.isActive)
        .map((t) => ({ id: t.id, label: t.displayName }));
    }
    return [];
  }, [targetType, filterTypeKey, resources, groups, resourceTypes]);

  // Reset target + type filter when target type changes.
  const handleTargetTypeChange = (v: string) => {
    setTargetType(v as ScopeTargetType);
    setTargetId("");
    setFilterTypeKey("");
  };

  // If the active type filter no longer matches any options (e.g. data refresh), clear the
  // selected target to avoid a stale id. Render-phase, not an effect (see useEntityFormDialog.ts).
  if (targetId && !targetOptions.some((o) => o.id === targetId)) {
    setTargetId("");
  }

  const showTypeFilter =
    targetType === "resource" || targetType === "resource_group";

  return (
    <div className="space-y-2 rounded-lg border p-3 bg-muted/30">
      <p className="text-xs font-medium text-muted-foreground">New override</p>

      <div
        className={cn(
          "grid gap-2",
          showTypeFilter ? "sm:grid-cols-4" : "sm:grid-cols-3",
        )}
      >
        <Select value={targetType} onValueChange={handleTargetTypeChange}>
          <SelectTrigger className="h-8 text-xs">
            <SelectValue placeholder="Target type" />
          </SelectTrigger>
          <SelectContent>
            {TARGET_TYPES.map((t) => (
              <SelectItem key={t.value} value={t.value} className="text-xs">
                {t.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {showTypeFilter && (
          <Select
            value={filterTypeKey || ALL_TYPES_VALUE}
            onValueChange={(v) =>
              setFilterTypeKey(v === ALL_TYPES_VALUE ? "" : v)
            }
            disabled={typeFilterOptions.length === 0}
          >
            <SelectTrigger className="h-8 text-xs">
              <SelectValue placeholder="All types" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_TYPES_VALUE} className="text-xs">
                All types
              </SelectItem>
              {typeFilterOptions.map((t) => (
                <SelectItem key={t.id} value={t.id} className="text-xs">
                  {t.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}

        <Combobox
          value={targetId}
          onChange={setTargetId}
          options={targetOptions}
          placeholder={targetOptions.length === 0 ? "Loading…" : "Select target"}
          searchPlaceholder="Search…"
          emptyText="No matches"
          disabled={targetOptions.length === 0}
          className="h-8 text-xs"
        />

        <Select value={effect} onValueChange={(v) => setEffect(v as ScopeEffect)}>
          <SelectTrigger className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SCOPE_EFFECTS.map((e) => (
              <SelectItem key={e.value} value={e.value} className="text-xs">
                {e.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {error && <p className="text-xs text-destructive">{error}</p>}

      <div className="flex justify-end">
        <Button
          type="button"
          size="sm"
          className="h-7 text-xs"
          loading={isSubmitting}
          disabled={!targetId || isSubmitting || !canEdit}
          onClick={() => void handleAdd()}
        >
          {!isSubmitting && <Plus className="h-3 w-3 mr-1" />}
          Add
        </Button>
      </div>
    </div>
  );
}

// Edit-mode wrapper: handles the add mutation against the server.
function ServerAddScopeForm({
  siteId,
  eventId,
  onAdded,
}: {
  siteId: string;
  eventId: string;
  onAdded: () => void;
}) {
  const [error, setError] = useState<string | null>(null);
  const addScope = useAddAvailabilityEventScope(siteId, eventId, {
    onSuccess: () => {
      setError(null);
      onAdded();
    },
    onError: (err) => setError(err.message),
  });

  return (
    <AddScopeForm
      onAdd={async (req) => { await addScope.mutateAsync(req); }}
      isSubmitting={addScope.isPending}
      error={error}
    />
  );
}

// ── Main dialog ──────────────────────────────────────────────────────────────

interface EventForm {
  title: string;
  eventType: AvailabilityEventType;
  defaultEffect: DefaultEffect;
  startLocal: string;
  endLocal: string;
  isRecurring: boolean;
  recurrenceRule: string;
  enabled: boolean;
  /** Overrides gathered while creating; sent with the create. Editing writes each at once. */
  draftScopes: ScopeDraft[];
}

const EMPTY_EVENT: EventForm = {
  title: "",
  eventType: "public_holiday",
  defaultEffect: "closed",
  startLocal: "",
  endLocal: "",
  isRecurring: false,
  recurrenceRule: "",
  enabled: true,
  draftScopes: [],
};

function fromEvent(event: AvailabilityEventInfo): EventForm {
  return {
    title: event.title,
    eventType: event.eventType,
    defaultEffect: event.defaultEffect,
    startLocal: toDateTimeLocalValue(event.startTs),
    endLocal: toDateTimeLocalValue(event.endTs),
    isRecurring: event.isRecurring,
    recurrenceRule: event.recurrenceRule ?? "",
    enabled: event.enabled,
    draftScopes: [],
  };
}

/** The rules the dialog states as a message; the first one broken is shown. */
function validateEvent(form: EventForm): string | null {
  if (!form.title.trim()) return "Title is required.";
  if (!form.startLocal || !form.endLocal) return "Start and end dates are required.";
  const start = new Date(form.startLocal).getTime();
  const end = new Date(form.endLocal).getTime();
  if (!Number.isFinite(start) || !Number.isFinite(end) || start >= end) {
    return "Start must be before end.";
  }
  if (form.isRecurring && !form.recurrenceRule.trim()) {
    return "Recurrence rule is required when recurring is enabled.";
  }
  return null;
}

export function AvailabilityEventDialog({ open, onOpenChange, siteId, event }: Props) {
  // The add-override form is transient: it closes whenever the dialog does.
  const [showAddScope, setShowAddScope] = useState(false);
  const handleOpenChange = (next: boolean) => {
    if (!next) setShowAddScope(false);
    onOpenChange(next);
  };

  const mutation = useSaveAvailabilityEvent(siteId);
  const { form, set, setForm, isDirty, error, submit, isSubmitting } = useEntityFormDialog({
    open,
    onOpenChange: handleOpenChange,
    entity: event,
    emptyForm: () => EMPTY_EVENT,
    toForm: fromEvent,
    mutation,
    validate: validateEvent,
    toVariables: (f: EventForm, e: AvailabilityEventInfo | null) => {
      const data: CreateAvailabilityEventRequest & UpdateAvailabilityEventRequest = {
        title: f.title.trim(),
        eventType: f.eventType,
        defaultEffect: f.defaultEffect,
        startTs: new Date(f.startLocal).toISOString(),
        endTs: new Date(f.endLocal).toISOString(),
        isRecurring: f.isRecurring,
        recurrenceRule: f.isRecurring ? f.recurrenceRule.trim() : undefined,
        enabled: f.enabled,
      };
      // Only attach draft scopes when creating; in edit mode scopes are
      // managed server-side via add/delete mutations.
      if (!e && f.draftScopes.length > 0) data.scopes = f.draftScopes;
      return e ? { id: e.id, data } : { id: null, data };
    },
  });
  const { title, eventType, defaultEffect, startLocal, endLocal, isRecurring, recurrenceRule, enabled, draftScopes } = form;

  // Live scopes state (optimistic-ish: re-read from event prop, refreshed via query invalidation)
  const serverScopes = event?.scopes ?? [];

  return (
    <FormDialog
      open={open}
      onOpenChange={handleOpenChange}
      title={event ? "Edit Availability Event" : "Add Availability Event"}
      description="Define a period that changes resource availability for this site."
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={event ? "Update" : "Create"}
      error={error}
      dirty={isDirty}
    >
          {/* Title */}
          <div className="space-y-1.5">
            <Label htmlFor="ae-title">Title</Label>
            <Input
              id="ae-title"
              value={title}
              onChange={(e) => set({ title: e.target.value })}
              placeholder="e.g., Christmas shutdown"
            />
          </div>

          {/* Type + Default effect */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="ae-type">Type</Label>
              <Select value={eventType} onValueChange={(v) => set({ eventType: v as AvailabilityEventType })}>
                <SelectTrigger id="ae-type"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {EVENT_TYPES.map((t) => (
                    <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="ae-effect">Default effect</Label>
              <Select value={defaultEffect} onValueChange={(v) => set({ defaultEffect: v as DefaultEffect })}>
                <SelectTrigger id="ae-effect"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {DEFAULT_EFFECTS.map((e) => (
                    <SelectItem key={e.value} value={e.value}>{e.label}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {defaultEffect === "closed"
                  ? "All resources closed unless overridden."
                  : "All resources available unless overridden."}
              </p>
            </div>
          </div>

          {/* Dates */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="ae-start">Start</Label>
              <DateTimePicker
                id="ae-start"
                value={startLocal}
                onChange={(value) => set({ startLocal: value })}
                placeholder="Pick start time"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="ae-end">End</Label>
              <DateTimePicker
                id="ae-end"
                value={endLocal}
                onChange={(value) => set({ endLocal: value })}
                placeholder="Pick end time"
              />
            </div>
          </div>

          {/* Toggles */}
          <div className="flex items-center justify-between">
            <Label htmlFor="ae-enabled" className="cursor-pointer">Enabled</Label>
            <Switch id="ae-enabled" checked={enabled} onCheckedChange={(checked) => set({ enabled: checked })} />
          </div>
          <div className="flex items-center justify-between">
            <Label htmlFor="ae-recurring" className="cursor-pointer">Recurring</Label>
            <Switch id="ae-recurring" checked={isRecurring} onCheckedChange={(checked) => set({ isRecurring: checked })} />
          </div>
          {isRecurring && (
            <div className="space-y-1.5">
              <Label htmlFor="ae-rrule">Recurrence rule (RRULE)</Label>
              <Input
                id="ae-rrule"
                value={recurrenceRule}
                onChange={(e) => set({ recurrenceRule: e.target.value })}
                placeholder="FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=24"
              />
            </div>
          )}

          {/* ── Scope overrides (available in both create and edit modes) ── */}
          <div className="space-y-2 pt-1">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium">Scope overrides</p>
                <p className="text-xs text-muted-foreground">
                  Override the default effect for specific resources, groups, or types.
                </p>
              </div>
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="h-7 text-xs"
                onClick={() => setShowAddScope((v) => !v)}
              >
                <Plus className="h-3 w-3 mr-1" />
                Add override
              </Button>
            </div>

            {showAddScope && (
              event ? (
                <ServerAddScopeForm
                  siteId={siteId}
                  eventId={event.id}
                  onAdded={() => setShowAddScope(false)}
                />
              ) : (
                <AddScopeForm
                  onAdd={(req) => {
                    setForm((prev) => ({ ...prev, draftScopes: [...prev.draftScopes, req] }));
                    setShowAddScope(false);
                  }}
                />
              )
            )}

            {event ? (
              serverScopes.length === 0 && !showAddScope ? (
                <p className="text-xs text-muted-foreground py-1">
                  No overrides — default effect applies to all resources.
                </p>
              ) : (
                <div className="divide-y border rounded-lg px-3">
                  {serverScopes.map((scope) => (
                    <ServerScopeRow
                      key={scope.id}
                      siteId={siteId}
                      eventId={event.id}
                      scope={scope}
                    />
                  ))}
                </div>
              )
            ) : (
              draftScopes.length === 0 && !showAddScope ? (
                <p className="text-xs text-muted-foreground py-1">
                  No overrides — default effect applies to all resources.
                </p>
              ) : (
                <div className="divide-y border rounded-lg px-3">
                  {draftScopes.map((scope, idx) => (
                    <ScopeRow
                      key={`${scope.targetType}:${scope.targetId}:${idx}`}
                      scope={scope}
                      onDelete={() =>
                        setForm((prev) => ({
                          ...prev,
                          draftScopes: prev.draftScopes.filter((_, i) => i !== idx),
                        }))
                      }
                    />
                  ))}
                </div>
              )
            )}
          </div>
    </FormDialog>
  );
}

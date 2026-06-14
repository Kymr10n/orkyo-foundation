import { useEffect, useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { cn } from "@foundation/src/lib/utils";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
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
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Badge } from "@foundation/src/components/ui/badge";
import { Combobox, type ComboboxOption } from "@foundation/src/components/ui/combobox";
import { DateTimePicker } from "@foundation/src/components/ui/date-time-picker";
import { Loader2, Plus, X } from "lucide-react";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { getResourceGroups } from "@foundation/src/lib/api/resource-groups-api";
import { apiGet } from "@foundation/src/lib/api/../core/api-client";
import { API_PATHS } from "@foundation/src/lib/core/api-paths";
import {
  addAvailabilityEventScope,
  deleteAvailabilityEventScope,
  type AvailabilityEventInfo,
  type AvailabilityEventScopeInfo,
  type AvailabilityEventType,
  type DefaultEffect,
  type ScopeEffect,
  type ScopeTargetType,
  type CreateAvailabilityEventRequest,
  type UpdateAvailabilityEventRequest,
} from "@foundation/src/lib/api/availability-events-api";

interface ScopeDraft {
  targetType: ScopeTargetType;
  targetId: string;
  effect: ScopeEffect;
}

interface ResourceTypeInfo {
  id: string;
  key: string;
  displayName: string;
  isActive: boolean;
}

type EventFormData = CreateAvailabilityEventRequest | UpdateAvailabilityEventRequest;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  siteId: string;
  event: AvailabilityEventInfo | null;
  onSave: (data: EventFormData) => Promise<void>;
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

function toDateTimeLocal(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// ── Shared scope-picker data ─────────────────────────────────────────────────

function useScopePickerOptions() {
  const { data: resources } = useQuery({
    queryKey: ["resources-all"],
    queryFn: () => getResources({ isActive: true }).then((r) => r.data),
    staleTime: 60_000,
  });
  const { data: groups } = useQuery({
    queryKey: ["resource-groups-all"],
    queryFn: async () => {
      const types = await apiGet<ResourceTypeInfo[]>(API_PATHS.RESOURCE_TYPES);
      const allGroups = await Promise.all(
        types.filter((t) => t.isActive).map((t) => getResourceGroups(t.key)),
      );
      return allGroups.flat();
    },
    staleTime: 60_000,
  });
  const { data: resourceTypes } = useQuery({
    queryKey: ["resource-types"],
    queryFn: () => apiGet<ResourceTypeInfo[]>(API_PATHS.RESOURCE_TYPES),
    staleTime: 300_000,
  });
  return { resources, groups, resourceTypes };
}

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
        disabled={isDeleting}
        aria-label="Remove override"
      >
        {isDeleting ? (
          <Loader2 className="h-3 w-3 animate-spin" />
        ) : (
          <X className="h-3 w-3" />
        )}
      </Button>
    </div>
  );
}

// Edit-mode wrapper: handles the delete mutation against the server.
function ServerScopeRow({
  siteId,
  eventId,
  scope,
  onDeleted,
}: {
  siteId: string;
  eventId: string;
  scope: AvailabilityEventScopeInfo;
  onDeleted: () => void;
}) {
  const deleteScope = useMutation({
    mutationFn: () => deleteAvailabilityEventScope(siteId, eventId, scope.id),
    onSuccess: onDeleted,
  });

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

  // If the active type filter no longer matches any options (e.g. data
  // refresh), clear the selected target to avoid a stale id.
  useEffect(() => {
    if (targetId && !targetOptions.some((o) => o.id === targetId)) {
      setTargetId("");
    }
  }, [targetOptions, targetId]);

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
          disabled={!targetId || isSubmitting || !canEdit}
          onClick={() => void handleAdd()}
        >
          {isSubmitting ? (
            <Loader2 className="h-3 w-3 mr-1 animate-spin" />
          ) : (
            <Plus className="h-3 w-3 mr-1" />
          )}
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
  const addScope = useMutation({
    mutationFn: (req: ScopeDraft) =>
      addAvailabilityEventScope(siteId, eventId, req),
    onSuccess: () => {
      setError(null);
      onAdded();
    },
    onError: (err: Error) => setError(err.message),
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

export function AvailabilityEventDialog({ open, onOpenChange, siteId, event, onSave }: Props) {
  const queryClient = useQueryClient();

  const [title, setTitle] = useState("");
  const [eventType, setEventType] = useState<AvailabilityEventType>("public_holiday");
  const [defaultEffect, setDefaultEffect] = useState<DefaultEffect>("closed");
  const [startLocal, setStartLocal] = useState("");
  const [endLocal, setEndLocal] = useState("");
  const [isRecurring, setIsRecurring] = useState(false);
  const [recurrenceRule, setRecurrenceRule] = useState("");
  const [enabled, setEnabled] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showAddScope, setShowAddScope] = useState(false);
  const [draftScopes, setDraftScopes] = useState<ScopeDraft[]>([]);

  // Live scopes state (optimistic-ish: re-read from event prop, refreshed via query invalidation)
  const serverScopes = event?.scopes ?? [];

  useEffect(() => {
    if (!open) return;
    setTitle(event?.title ?? "");
    setEventType(event?.eventType ?? "public_holiday");
    setDefaultEffect(event?.defaultEffect ?? "closed");
    setStartLocal(event ? toDateTimeLocal(event.startTs) : "");
    setEndLocal(event ? toDateTimeLocal(event.endTs) : "");
    setIsRecurring(event?.isRecurring ?? false);
    setRecurrenceRule(event?.recurrenceRule ?? "");
    setEnabled(event?.enabled ?? true);
    setError(null);
    setShowAddScope(false);
    setDraftScopes([]);
  }, [event, open]);

  const handleSubmit = async (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);
    if (!title.trim()) { setError("Title is required."); return; }
    if (!startLocal || !endLocal) { setError("Start and end dates are required."); return; }
    const start = new Date(startLocal).getTime();
    const end = new Date(endLocal).getTime();
    if (!Number.isFinite(start) || !Number.isFinite(end) || start >= end) {
      setError("Start must be before end."); return;
    }
    if (isRecurring && !recurrenceRule.trim()) {
      setError("Recurrence rule is required when recurring is enabled."); return;
    }
    setSaving(true);
    try {
      const payload: CreateAvailabilityEventRequest & UpdateAvailabilityEventRequest = {
        title: title.trim(),
        eventType,
        defaultEffect,
        startTs: new Date(startLocal).toISOString(),
        endTs: new Date(endLocal).toISOString(),
        isRecurring,
        recurrenceRule: isRecurring ? recurrenceRule.trim() : undefined,
        enabled,
      };
      // Only attach draft scopes when creating; in edit mode scopes are
      // managed server-side via add/delete mutations.
      if (!event && draftScopes.length > 0) {
        payload.scopes = draftScopes;
      }
      await onSave(payload);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save.");
    } finally {
      setSaving(false);
    }
  };

  const invalidateEvent = () => {
    void queryClient.invalidateQueries({ queryKey: ["availability-events"] });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{event ? "Edit Availability Event" : "Add Availability Event"}</DialogTitle>
          <DialogDescription>
            Define a period that changes resource availability for this site.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}

          {/* Title */}
          <div className="space-y-1.5">
            <Label htmlFor="ae-title">Title</Label>
            <Input
              id="ae-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g., Christmas shutdown"
            />
          </div>

          {/* Type + Default effect */}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="ae-type">Type</Label>
              <Select value={eventType} onValueChange={(v) => setEventType(v as AvailabilityEventType)}>
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
              <Select value={defaultEffect} onValueChange={(v) => setDefaultEffect(v as DefaultEffect)}>
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
                onChange={setStartLocal}
                placeholder="Pick start time"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="ae-end">End</Label>
              <DateTimePicker
                id="ae-end"
                value={endLocal}
                onChange={setEndLocal}
                placeholder="Pick end time"
              />
            </div>
          </div>

          {/* Toggles */}
          <div className="flex items-center justify-between">
            <Label htmlFor="ae-enabled" className="cursor-pointer">Enabled</Label>
            <Switch id="ae-enabled" checked={enabled} onCheckedChange={setEnabled} />
          </div>
          <div className="flex items-center justify-between">
            <Label htmlFor="ae-recurring" className="cursor-pointer">Recurring</Label>
            <Switch id="ae-recurring" checked={isRecurring} onCheckedChange={setIsRecurring} />
          </div>
          {isRecurring && (
            <div className="space-y-1.5">
              <Label htmlFor="ae-rrule">Recurrence rule (RRULE)</Label>
              <Input
                id="ae-rrule"
                value={recurrenceRule}
                onChange={(e) => setRecurrenceRule(e.target.value)}
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
                  onAdded={() => {
                    setShowAddScope(false);
                    invalidateEvent();
                  }}
                />
              ) : (
                <AddScopeForm
                  onAdd={(req) => {
                    setDraftScopes((prev) => [...prev, req]);
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
                      onDeleted={invalidateEvent}
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
                        setDraftScopes((prev) => prev.filter((_, i) => i !== idx))
                      }
                    />
                  ))}
                </div>
              )
            )}
          </div>

          <div className="flex justify-end gap-3 pt-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={saving}>
              {saving && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
              {event ? "Update" : "Create"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

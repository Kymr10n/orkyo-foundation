import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { getResources, type ResourceInfo } from "@foundation/src/lib/api/resources-api";
import {
  getAssignmentsByRequest,
  createAssignment,
  cancelAssignment,
  validateAssignment,
  hardBlockers,
  softBlockers,
  type ResourceAssignmentInfo,
  type ValidationResult,
} from "@foundation/src/lib/api/resource-assignments-api";
import { ValidationIssueList } from "./ValidationIssueList";
import { Plus, Trash2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";

interface RequestPeopleSectionProps {
  /** ID of an existing request. Undefined when creating a new request. */
  requestId?: string;
  /** ISO datetime for the request start (used for validation). */
  requestStartTs?: string;
  /** ISO datetime for the request end (used for validation). */
  requestEndTs?: string;
  /** Called whenever the blocker state changes so the parent can disable Save. */
  onBlockersChange: (hasBlockers: boolean) => void;
}

interface PendingRow {
  /** Unique key for React rendering */
  key: string;
  resourceId: string;
  allocationPercent: number | null;  // null for Exclusive resources
  role: string;
  notes: string;
  validating: boolean;
  validationResult: ValidationResult | null;
  saving: boolean;
  error: string | null;
}

export function RequestPeopleSection({
  requestId,
  requestStartTs,
  requestEndTs,
  onBlockersChange,
}: RequestPeopleSectionProps) {
  const [people, setPeople] = useState<ResourceInfo[]>([]);
  const [assignments, setAssignments] = useState<ResourceAssignmentInfo[]>([]);
  const [pendingRows, setPendingRows] = useState<PendingRow[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const debounceTimers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());

  // Load available people and existing assignments
  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      try {
        const [peopleRes, assignmentsRes] = await Promise.all([
          getResources({ resourceTypeKey: 'person', isActive: true }),
          requestId ? getAssignmentsByRequest(requestId) : Promise.resolve([]),
        ]);
        if (!cancelled) {
          setPeople(peopleRes.data);
          setAssignments(assignmentsRes.filter((a) => a.resourceTypeKey === 'person'));
        }
      } catch {
        // Non-critical: section remains empty
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [requestId]);

  // Recompute blocker state whenever pending rows change. Only HARD blockers gate
  // saving — capability.missing / overbooked are soft (see SOFT_BLOCKER_CODES) and
  // the backend accepts them, so they must not disable Save here either.
  useEffect(() => {
    const hasBlockers = pendingRows.some(
      (r) => r.validationResult && hardBlockers(r.validationResult).length > 0
    );
    onBlockersChange(hasBlockers);
  }, [pendingRows, onBlockersChange]);

  const addPendingRow = () => {
    setPendingRows((prev) => [
      ...prev,
      {
        key: crypto.randomUUID(),
        resourceId: '',
        allocationPercent: 100,
        role: '',
        notes: '',
        validating: false,
        validationResult: null,
        saving: false,
        error: null,
      },
    ]);
  };

  const removePendingRow = (key: string) => {
    const timer = debounceTimers.current.get(key);
    if (timer) clearTimeout(timer);
    debounceTimers.current.delete(key);
    setPendingRows((prev) => prev.filter((r) => r.key !== key));
  };

  const updatePendingRow = (key: string, patch: Partial<Omit<PendingRow, 'key'>>) => {
    setPendingRows((prev) =>
      prev.map((r) => (r.key === key ? { ...r, ...patch } : r))
    );
  };

  const scheduleValidation = (row: PendingRow, updatedRow: PendingRow) => {
    const timer = debounceTimers.current.get(row.key);
    if (timer) clearTimeout(timer);

    if (!updatedRow.resourceId || !requestStartTs || !requestEndTs) return;

    const t = setTimeout(async () => {
      updatePendingRow(row.key, { validating: true, validationResult: null });
      try {
        // requestId omitted when creating a new request — the backend validator
        // runs in dry-run mode (skips capability checks, still flags off-time /
        // overbook / non-working-day issues).
        const result = await validateAssignment({
          requestId,
          resourceId: updatedRow.resourceId,
          startUtc: requestStartTs,
          endUtc: requestEndTs,
          allocationPercent: updatedRow.allocationPercent ?? undefined,
        });
        updatePendingRow(row.key, { validating: false, validationResult: result });
      } catch {
        updatePendingRow(row.key, { validating: false, validationResult: null });
      }
    }, 400);

    debounceTimers.current.set(row.key, t);
  };

  const handlePersonChange = (key: string, resourceId: string) => {
    const row = pendingRows.find((r) => r.key === key)!;
    const person = people.find((p) => p.id === resourceId);
    const allocationPercent = person?.allocationMode === 'Exclusive' ? null : (row.allocationPercent ?? 100);
    const updated = { ...row, resourceId, allocationPercent };
    updatePendingRow(key, { resourceId, allocationPercent });
    scheduleValidation(row, updated);
  };

  const handleAllocationChange = (key: string, value: string) => {
    const parsed = parseInt(value.replace(/\D/g, ''), 10);
    const allocationPercent: number | null = isNaN(parsed) ? 0 : Math.min(100, Math.max(0, parsed));
    const row = pendingRows.find((r) => r.key === key)!;
    const updated = { ...row, allocationPercent };
    updatePendingRow(key, { allocationPercent });
    scheduleValidation(row, updated);
  };

  const handleSaveRow = async (key: string) => {
    if (!requestId || !requestStartTs || !requestEndTs) {
      updatePendingRow(key, { error: 'Request must be saved with a schedule before adding people.' });
      return;
    }
    const row = pendingRows.find((r) => r.key === key);
    if (!row?.resourceId) return;
    if (row.validationResult && hardBlockers(row.validationResult).length > 0) return;

    updatePendingRow(key, { saving: true, error: null });
    try {
      const created = await createAssignment({
        requestId,
        resourceId: row.resourceId,
        startUtc: requestStartTs,
        endUtc: requestEndTs,
        allocationPercent: row.allocationPercent ?? undefined,
      });
      setAssignments((prev) => [...prev, created]);
      removePendingRow(key);
    } catch (err) {
      updatePendingRow(key, { saving: false, error: err instanceof Error ? err.message : 'Failed to save' });
    }
  };

  const handleRemoveAssignment = async (id: string) => {
    try {
      await cancelAssignment(id);
      setAssignments((prev) => prev.filter((a) => a.id !== id));
    } catch {
      // Assignment may already be cancelled; refresh on next open
    }
  };

  const assignedIds = new Set(assignments.map((a) => a.resourceId));
  const alreadyPickedIds = new Set(pendingRows.map((r) => r.resourceId).filter(Boolean));

  return (
    <div>
      <div className="flex items-center gap-2">
        <h4 className="text-sm font-medium">People</h4>
        <Badge variant="outline" className="text-xs">
          {assignments.length} assigned
        </Badge>
      </div>
      <div className="space-y-3 pt-4">
          {isLoading && (
            <p className="text-xs text-muted-foreground">Loading…</p>
          )}

          {/* Existing assignments */}
          {assignments.map((a) => {
            const person = people.find((p) => p.id === a.resourceId);
            return (
              <div key={a.id} className="flex items-center justify-between rounded-md border px-3 py-2 text-sm" data-testid="assignment-row">
                <span>{person?.name ?? a.resourceId}</span>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label="Remove assignment"
                  onClick={() => handleRemoveAssignment(a.id)}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            );
          })}

          {/* Pending (unsaved) rows */}
          {pendingRows.map((row) => {
            const selectedPerson = people.find((p) => p.id === row.resourceId);
            const isExclusive = selectedPerson?.allocationMode === 'Exclusive';
            return (
            <div key={row.key} className="space-y-2 rounded-md border p-3" data-testid="pending-row">
              <div className={`grid gap-2 ${isExclusive ? 'grid-cols-1' : 'grid-cols-[1fr_80px]'}`}>
                <div className="space-y-1">
                  <Label className="text-xs">Person</Label>
                  <Select
                    value={row.resourceId}
                    onValueChange={(v) => handlePersonChange(row.key, v)}
                  >
                    <SelectTrigger data-testid="person-select">
                      <SelectValue placeholder="Select a person" />
                    </SelectTrigger>
                    <SelectContent>
                      {people
                        .filter((p) => !assignedIds.has(p.id) && (!alreadyPickedIds.has(p.id) || p.id === row.resourceId))
                        .map((p) => (
                          <SelectItem key={p.id} value={p.id}>
                            {p.name}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                </div>
                {!isExclusive && (
                  <div className="space-y-1">
                    <Label className="text-xs">Alloc %</Label>
                    <Input
                      type="text"
                      inputMode="numeric"
                      value={row.allocationPercent ?? ''}
                      onChange={(e) => handleAllocationChange(row.key, e.target.value)}
                      data-testid="allocation-input"
                    />
                  </div>
                )}
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div className="space-y-1">
                  <Label className="text-xs">Role (optional)</Label>
                  <Input
                    value={row.role}
                    onChange={(e) => updatePendingRow(row.key, { role: e.target.value })}
                    placeholder="e.g. Lead"
                    data-testid="role-input"
                  />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Notes (optional)</Label>
                  <Input
                    value={row.notes}
                    onChange={(e) => updatePendingRow(row.key, { notes: e.target.value })}
                    placeholder="Optional"
                    data-testid="notes-input"
                  />
                </div>
              </div>

              {row.validating && (
                <p className="text-xs text-muted-foreground" data-testid="validating-indicator">Validating…</p>
              )}

              {row.validationResult && (
                <div data-testid="validation-feedback">
                  {/* Soft blockers (capability.missing / overbooked) render as warnings —
                      the assignment can still be saved. */}
                  <ValidationIssueList issues={hardBlockers(row.validationResult)} variant="blocker" />
                  <ValidationIssueList
                    issues={[...softBlockers(row.validationResult), ...row.validationResult.warnings]}
                    variant="warning"
                  />
                  {row.validationResult.severity === 'ok' && row.validationResult.blockers.length === 0 && row.validationResult.warnings.length === 0 && (
                    <p className="text-xs text-green-600" data-testid="validation-ok">No issues found</p>
                  )}
                </div>
              )}

              {row.error && (
                <p className="text-xs text-destructive" data-testid="row-error">{row.error}</p>
              )}

              <div className="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => removePendingRow(row.key)}
                >
                  Cancel
                </Button>
                <Button
                  type="button"
                  size="sm"
                  disabled={
                    !row.resourceId ||
                    row.saving ||
                    !!(row.validationResult && hardBlockers(row.validationResult).length > 0)
                  }
                  data-testid="save-row-btn"
                  onClick={() => handleSaveRow(row.key)}
                >
                  {row.saving ? 'Saving…' : 'Add'}
                </Button>
              </div>
            </div>
            );
          })}

          <Button
            type="button"
            variant="outline"
            size="sm"
            className="w-full"
            onClick={addPendingRow}
            data-testid="add-person-btn"
          >
            <Plus className="h-4 w-4 mr-1" />
            Add Person
          </Button>
      </div>
    </div>
  );
}

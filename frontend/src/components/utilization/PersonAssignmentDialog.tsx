import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { Badge } from "@foundation/src/components/ui/badge";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Button } from "@foundation/src/components/ui/button";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { Input } from "@foundation/src/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import {
  createAssignment,
  cancelAssignment,
  validateAssignment,
  validateAssignmentsBatch,
  SOFT_BLOCKER_CODES,
  type ValidationResult,
} from "@foundation/src/lib/api/resource-assignments-api";
import {
  getPersonAssignmentOptions,
  mismatchCount,
  type PersonAssignmentOption,
} from "@foundation/src/lib/api/person-candidate-requests-api";
import { ValidationIssueList } from "../requests/ValidationIssueList";
import { ALLOCATION_MODE } from "@foundation/src/constants/allocation-mode";

export interface PersonAssignmentDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  personId: string;
  personName: string;
  /** Resource allocation mode — drives whether an allocation percent is sent. */
  allocationMode: string;
  /** ISO datetime for the clicked segment's start. */
  start: string;
  /** ISO datetime for the clicked segment's end. */
  end: string;
}

/**
 * The window an assignment should occupy: the request's own scheduled window when it
 * has one, else the clicked grid segment. Using the segment window for a scheduled
 * request over-allocates the resource (a 4h request would span the whole visible slice,
 * inflating its time-weighted utilization), so the request window is preferred.
 */
function assignmentWindow(
  option: PersonAssignmentOption,
  segmentStart: string,
  segmentEnd: string,
): { startUtc: string; endUtc: string } {
  return option.startTs && option.endTs
    ? { startUtc: option.startTs, endUtc: option.endTs }
    : { startUtc: segmentStart, endUtc: segmentEnd };
}

/**
 * Reduce a validation result to the conflict issues we surface on an assigned row —
 * capability mismatch and overbook (soft blockers) plus any warnings. Returns null when
 * the assignment is clean, so callers can treat the map entry as "has conflicts".
 */
function conflictIssuesOf(result: ValidationResult): ValidationResult | null {
  const issues = [
    ...result.blockers.filter((b) => SOFT_BLOCKER_CODES.has(b.code)),
    ...result.warnings.filter((w) => SOFT_BLOCKER_CODES.has(w.code)),
  ];
  return issues.length > 0 ? { severity: "warning", blockers: [], warnings: issues } : null;
}

type ItemStatus =
  | { kind: "idle" }
  | { kind: "validating" }
  | { kind: "saving" }
  | { kind: "removing" }
  | { kind: "feedback"; result: ValidationResult; isBlocker: boolean };

function formatPeriod(start: string, end: string): string {
  if (!start || !end) return "";
  const fmt = new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
  return `${fmt.format(new Date(start))} – ${fmt.format(new Date(end))}`;
}

/** Compact duration between two ISO datetimes, e.g. "45m", "5h", "1h 30m". */
function formatSpan(start: string, end: string): string {
  const totalMinutes = Math.round(
    (new Date(end).getTime() - new Date(start).getTime()) / 60_000,
  );
  if (!Number.isFinite(totalMinutes) || totalMinutes <= 0) return "";
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) return `${minutes}m`;
  if (minutes === 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
}

/**
 * Where a request sits within the dialog's window, as left/width percentages for a mini
 * timeline bar. Clamps to [0, 100] so requests that start before / end after the scope
 * still render at the edges, and floors the width so a near-instant request stays visible.
 * Returns null for an invalid/zero-length window.
 */
function timelineExtent(
  winStart: string,
  winEnd: string,
  reqStart: string,
  reqEnd: string,
): { leftPct: number; widthPct: number } | null {
  const w0 = new Date(winStart).getTime();
  const w1 = new Date(winEnd).getTime();
  const span = w1 - w0;
  if (!Number.isFinite(span) || span <= 0) return null;

  const clamp = (n: number) => Math.min(100, Math.max(0, n));
  const round = (n: number) => Math.round(n * 100) / 100;

  const left = clamp(((new Date(reqStart).getTime() - w0) / span) * 100);
  const right = clamp(((new Date(reqEnd).getTime() - w0) / span) * 100);
  return { leftPct: round(left), widthPct: round(Math.max(right - left, 2)) };
}

export function PersonAssignmentDialog({
  open,
  onOpenChange,
  personId,
  personName,
  allocationMode,
  start,
  end,
}: PersonAssignmentDialogProps) {
  // Exclusive resources take the whole slot (no percent); fractional/capacity
  // resources require an allocation percent — default to a full booking here,
  // mirroring RequestPeopleSection. The quick-add dialog has no percent input.
  const allocationPercent =
    allocationMode === ALLOCATION_MODE.EXCLUSIVE ? undefined : 100;
  const [options, setOptions] = useState<PersonAssignmentOption[]>([]);
  const [itemStatus, setItemStatus] = useState<Map<string, ItemStatus>>(new Map());
  // requestId → conflict issues for an already-assigned row (capability / overbook),
  // computed on load so conflicts persist across reopens, not just after toggling.
  const [conflicts, setConflicts] = useState<Map<string, ValidationResult>>(new Map());
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const queryClient = useQueryClient();

  // Refresh the utilization grid after an assignment changes. The grid keys its
  // data by window + granularity (see PeopleUtilizationGrid); a prefix-match
  // invalidation catches every active variant so the availability % and segment
  // badges refetch. The capability-conflicts query keys off the assignments and
  // refetches transitively.
  const invalidateGridQueries = () => {
    queryClient.invalidateQueries({ queryKey: ["utilization-by-resource"] });
    queryClient.invalidateQueries({ queryKey: ["resource-assignments-by-type"] });
  };

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setSearch("");
    setItemStatus(new Map());
    setConflicts(new Map());
    setLoadError(null);
    setIsLoading(true);
    getPersonAssignmentOptions(personId, start, end)
      .then((opts) => {
        if (cancelled) return;
        setOptions(
          [...opts].sort((a, b) => {
            const aAssigned = a.assignmentId !== null ? 0 : 1;
            const bAssigned = b.assignmentId !== null ? 0 : 1;
            if (aAssigned !== bAssigned) return aAssigned - bAssigned;
            return a.name.localeCompare(b.name);
          }),
        );
        // Surface existing conflicts on the already-assigned rows. Decorative — runs
        // in the background; each assignment is excluded from its own overbook check.
        void loadConflicts(opts, cancelled);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : "Failed to load");
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, personId, start, end]);

  const loadConflicts = async (opts: PersonAssignmentOption[], cancelled: boolean) => {
    const assigned = opts.filter(
      (o) => o.assignmentId !== null && o.startTs && o.endTs,
    );
    if (assigned.length === 0) return;
    try {
      const results = await validateAssignmentsBatch(
        assigned.map((o) => {
          const win = assignmentWindow(o, start, end);
          return {
            requestId: o.requestId,
            resourceId: personId,
            startUtc: win.startUtc,
            endUtc: win.endUtc,
            allocationPercent,
            excludeAssignmentId: o.assignmentId ?? undefined,
          };
        }),
      );
      if (cancelled) return;
      const map = new Map<string, ValidationResult>();
      for (const item of results) {
        if (!item.requestId) continue;
        const conflict = conflictIssuesOf(item.result);
        if (conflict) map.set(item.requestId, conflict);
      }
      setConflicts(map);
    } catch {
      // Conflict badges are decorative — ignore validation failures.
    }
  };

  const filteredOptions = useMemo(() => {
    if (!search.trim()) return options;
    const q = search.toLowerCase();
    return options.filter((o) => o.name.toLowerCase().includes(q));
  }, [options, search]);

  const assignedCount = useMemo(
    () => options.filter((o) => o.assignmentId !== null).length,
    [options],
  );

  // Header summary: how many assigned requests are currently conflicted.
  const conflictCount = useMemo(
    () => options.filter((o) => o.assignmentId !== null && conflicts.has(o.requestId)).length,
    [options, conflicts],
  );

  const patchConflict = (requestId: string, conflict: ValidationResult | null) =>
    setConflicts((prev) => {
      const next = new Map(prev);
      if (conflict) next.set(requestId, conflict);
      else next.delete(requestId);
      return next;
    });

  const patchStatus = (requestId: string, status: ItemStatus) =>
    setItemStatus((prev) => new Map(prev).set(requestId, status));

  const handleToggle = async (option: PersonAssignmentOption) => {
    const status = itemStatus.get(option.requestId) ?? { kind: "idle" as const };
    if (status.kind !== "idle" && status.kind !== "feedback") return;

    if (option.assignmentId !== null) {
      patchStatus(option.requestId, { kind: "removing" });
      try {
        await cancelAssignment(option.assignmentId);
        setOptions((prev) =>
          prev.map((o) =>
            o.requestId === option.requestId ? { ...o, assignmentId: null } : o,
          ),
        );
        patchConflict(option.requestId, null);
        invalidateGridQueries();
      } catch {
        // Silently reset — assignment may already be cancelled
      }
      patchStatus(option.requestId, { kind: "idle" });
    } else {
      // Validate first, then assign — over the request's own window (see assignmentWindow).
      const win = assignmentWindow(option, start, end);
      patchStatus(option.requestId, { kind: "validating" });
      let result: ValidationResult;
      try {
        result = await validateAssignment({
          requestId: option.requestId,
          resourceId: personId,
          startUtc: win.startUtc,
          endUtc: win.endUtc,
          allocationPercent,
        });
      } catch {
        patchStatus(option.requestId, { kind: "idle" });
        return;
      }

      // Soft blockers don't stop the planner: capability.missing (assign despite a skill
      // mismatch) and assignment.overbooked (overbooking is a deliberate, surfaced state).
      // They are demoted to warnings and shown on the row / bar after assigning.
      const hardBlockers = result.blockers.filter((b) => !SOFT_BLOCKER_CODES.has(b.code));
      const softWarnings = result.blockers.filter((b) => SOFT_BLOCKER_CODES.has(b.code));
      const effectiveResult: ValidationResult = {
        ...result,
        blockers: hardBlockers,
        warnings: [...result.warnings, ...softWarnings],
      };

      if (hardBlockers.length > 0) {
        patchStatus(option.requestId, { kind: "feedback", result: effectiveResult, isBlocker: true });
        return;
      }

      patchStatus(option.requestId, { kind: "saving" });
      try {
        const created = await createAssignment({
          requestId: option.requestId,
          resourceId: personId,
          startUtc: win.startUtc,
          endUtc: win.endUtc,
          allocationPercent,
        });
        setOptions((prev) =>
          prev.map((o) =>
            o.requestId === option.requestId ? { ...o, assignmentId: created.id } : o,
          ),
        );
        invalidateGridQueries();
        // Persist any conflict (capability / overbook) on the now-assigned row so the
        // badge + reasons survive — one source of truth with the on-load conflicts map.
        patchConflict(option.requestId, conflictIssuesOf(effectiveResult));
        patchStatus(option.requestId, { kind: "idle" });
      } catch {
        patchStatus(option.requestId, { kind: "idle" });
      }
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg" data-testid="person-assignment-dialog">
        <DialogHeader className="shrink-0">
          <DialogTitle>Assignments — {personName}</DialogTitle>
          <DialogDescription>{formatPeriod(start, end)}</DialogDescription>
        </DialogHeader>

        {!isLoading && options.length > 0 && (
          <Input
            placeholder="Filter by request name…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8 text-sm shrink-0"
            data-testid="request-filter-input"
          />
        )}

        <ScrollArea type="auto" className="flex-1 min-h-0 pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">Loading…</div>
            ) : loadError ? (
              <ErrorAlert message={loadError} />
            ) : options.length === 0 ? (
              <div className="text-center py-8 border rounded-lg border-dashed">
                <p className="text-sm text-muted-foreground">
                  No active requests overlap this period.
                </p>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between pb-2 border-b">
                  <span className="text-sm font-medium">Requests</span>
                  <div className="flex items-center gap-2">
                    {conflictCount > 0 && (
                      <Badge
                        variant="warning"
                        className="text-xs gap-1"
                        data-testid="conflict-summary-badge"
                      >
                        <AlertTriangle className="h-3 w-3" />
                        {conflictCount} conflicted
                      </Badge>
                    )}
                    <Badge variant="outline" className="text-xs">
                      {assignedCount} assigned
                    </Badge>
                  </div>
                </div>

                <div className="space-y-1">
                  {filteredOptions.map((option) => {
                    const status = itemStatus.get(option.requestId) ?? { kind: "idle" as const };
                    const isChecked = option.assignmentId !== null;
                    const isInFlight =
                      status.kind === "validating" ||
                      status.kind === "saving" ||
                      status.kind === "removing";
                    const mismatches = mismatchCount(option);
                    const hasFeedback = status.kind === "feedback";
                    const conflict = conflicts.get(option.requestId);
                    const showConflict = isChecked && !hasFeedback && conflict != null;

                    return (
                      <div key={option.requestId} data-testid="assignment-option-row">
                        <div
                          className="flex items-center gap-3 p-2 rounded-md hover:bg-muted/50 cursor-pointer select-none"
                          onClick={() => void handleToggle(option)}
                        >
                          <Checkbox
                            checked={isChecked}
                            disabled={isInFlight}
                            onCheckedChange={() => void handleToggle(option)}
                            onClick={(e) => e.stopPropagation()}
                            data-testid="assignment-checkbox"
                          />
                          <div className="flex-1 min-w-0">
                            <div className="flex items-baseline gap-2">
                              <p className="text-sm font-medium truncate">{option.name}</p>
                              {option.startTs && option.endTs && (
                                <span
                                  className="text-xs text-muted-foreground shrink-0 tabular-nums"
                                  data-testid="request-duration"
                                >
                                  {formatSpan(option.startTs, option.endTs)}
                                </span>
                              )}
                            </div>
                            {(option.startTs || option.endTs) && (
                              <p className="text-xs text-muted-foreground truncate">
                                {formatPeriod(option.startTs ?? "", option.endTs ?? "")}
                              </p>
                            )}
                            {option.startTs && option.endTs && (() => {
                              const extent = timelineExtent(
                                start,
                                end,
                                option.startTs,
                                option.endTs,
                              );
                              if (!extent) return null;
                              return (
                                <div
                                  className="mt-1 h-1 rounded bg-muted"
                                  data-testid="request-timeline"
                                  title={`When this request happens within ${formatPeriod(start, end)}`}
                                >
                                  <div
                                    className="h-full rounded bg-primary/70"
                                    style={{
                                      marginLeft: `${extent.leftPct}%`,
                                      width: `${extent.widthPct}%`,
                                    }}
                                    data-testid="request-timeline-fill"
                                  />
                                </div>
                              );
                            })()}
                          </div>
                          {isInFlight && (
                            <span className="text-xs text-muted-foreground shrink-0">
                              {status.kind === "validating"
                                ? "Checking…"
                                : status.kind === "saving"
                                  ? "Saving…"
                                  : "Removing…"}
                            </span>
                          )}
                          {!isInFlight && !isChecked && mismatches > 0 && (
                            <Badge
                              variant="warning"
                              className="text-xs shrink-0 gap-1"
                              data-testid="mismatch-badge"
                            >
                              <AlertTriangle className="h-3 w-3" />
                              {mismatches}
                            </Badge>
                          )}
                          {!isInFlight && showConflict && (
                            <Badge
                              variant="warning"
                              className="text-xs shrink-0 gap-1"
                              data-testid="conflict-badge"
                            >
                              <AlertTriangle className="h-3 w-3" />
                              {conflict.warnings.length}
                            </Badge>
                          )}
                        </div>
                        {hasFeedback && (
                          <div
                            className="ml-9 pb-1 pr-2 space-y-0.5"
                            data-testid="item-validation-feedback"
                          >
                            <ValidationIssueList
                              issues={status.result.blockers}
                              variant="blocker"
                            />
                            <ValidationIssueList
                              issues={status.result.warnings}
                              variant="warning"
                            />
                          </div>
                        )}
                        {showConflict && (
                          <div
                            className="ml-9 pb-1 pr-2 space-y-0.5"
                            data-testid="item-conflict-feedback"
                          >
                            <ValidationIssueList issues={conflict.warnings} variant="warning" />
                          </div>
                        )}
                      </div>
                    );
                  })}
                  {filteredOptions.length === 0 && search && (
                    <p className="text-sm text-muted-foreground text-center py-4">
                      No results for &ldquo;{search}&rdquo;
                    </p>
                  )}
                </div>
              </>
            )}
          </div>
        </ScrollArea>

        <DialogFooter className="shrink-0">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

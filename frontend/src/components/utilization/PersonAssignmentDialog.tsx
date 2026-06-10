import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { Badge } from "@foundation/src/components/ui/badge";
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
 * Validation codes that are surfaced as warnings rather than hard blockers in the
 * quick-assign flow — the planner may assign despite them. Mirrors the backend's
 * soft-constraint set in ResourceAssignmentService.
 */
const SOFT_BLOCKER_CODES: ReadonlySet<string> = new Set([
  "capability.missing",
  "assignment.overbooked",
]);

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
  }, [open, personId, start, end]);

  const filteredOptions = useMemo(() => {
    if (!search.trim()) return options;
    const q = search.toLowerCase();
    return options.filter((o) => o.name.toLowerCase().includes(q));
  }, [options, search]);

  const assignedCount = useMemo(
    () => options.filter((o) => o.assignmentId !== null).length,
    [options],
  );

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
        invalidateGridQueries();
      } catch {
        // Silently reset — assignment may already be cancelled
      }
      patchStatus(option.requestId, { kind: "idle" });
    } else {
      // Validate first, then assign.
      patchStatus(option.requestId, { kind: "validating" });
      let result: ValidationResult;
      try {
        result = await validateAssignment({
          requestId: option.requestId,
          resourceId: personId,
          startUtc: start,
          endUtc: end,
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
          startUtc: start,
          endUtc: end,
          allocationPercent,
        });
        setOptions((prev) =>
          prev.map((o) =>
            o.requestId === option.requestId ? { ...o, assignmentId: created.id } : o,
          ),
        );
        invalidateGridQueries();
        // Surface any warnings (including capability mismatches) so the planner is aware.
        if (effectiveResult.warnings.length > 0) {
          patchStatus(option.requestId, { kind: "feedback", result: effectiveResult, isBlocker: false });
        } else {
          patchStatus(option.requestId, { kind: "idle" });
        }
      } catch {
        patchStatus(option.requestId, { kind: "idle" });
      }
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[80vh] overflow-hidden" data-testid="person-assignment-dialog">
        <DialogHeader>
          <DialogTitle>Assignments — {personName}</DialogTitle>
          <DialogDescription>{formatPeriod(start, end)}</DialogDescription>
        </DialogHeader>

        {!isLoading && options.length > 0 && (
          <Input
            placeholder="Filter by request name…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8 text-sm"
            data-testid="request-filter-input"
          />
        )}

        <ScrollArea className="max-h-[55vh] pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">Loading…</div>
            ) : loadError ? (
              <div className="text-sm text-destructive bg-destructive/10 p-3 rounded-md">
                {loadError}
              </div>
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
                  <Badge variant="outline" className="text-xs">
                    {assignedCount} assigned
                  </Badge>
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
                              variant="outline"
                              className="text-xs text-amber-600 border-amber-300 shrink-0 gap-1"
                              data-testid="mismatch-badge"
                            >
                              <AlertTriangle className="h-3 w-3" />
                              {mismatches}
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

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

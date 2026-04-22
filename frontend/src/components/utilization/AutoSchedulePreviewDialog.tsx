import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { AlertTriangle, Loader2 } from "lucide-react";
import type { AutoSchedulePreviewResponse } from "@/lib/api/auto-schedule-api";

interface Props {
  open: boolean;
  preview: AutoSchedulePreviewResponse | null;
  isApplying?: boolean;
  applyError?: string | null;
  onApply: () => void;
  onClose: () => void;
}

const REASON_LABELS: Record<string, string> = {
  NoCompatibleSpace: "No compatible space",
  DateWindowTooTight: "Date window too tight",
  InsufficientCapacity: "Insufficient capacity",
  BlockedByFixedAssignments: "Blocked by existing assignments",
  InvalidDuration: "Invalid duration",
  MissingRequiredData: "Missing required data",
  InternalSolverLimit: "Solver limit reached",
};

export function AutoSchedulePreviewDialog({
  open,
  preview,
  isApplying,
  applyError,
  onApply,
  onClose,
}: Props) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Auto-schedule preview</DialogTitle>
          <DialogDescription>
            Review the proposed placements before committing them.
          </DialogDescription>
        </DialogHeader>

        {!preview ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        ) : (
          <div className="space-y-4">
            {/* Summary cards */}
            <section className="grid grid-cols-3 gap-3">
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">Solver</div>
                <div className="text-sm font-medium">
                  {preview.solverUsed === "OrToolsCpSat"
                    ? "OR-Tools CP-SAT"
                    : "Greedy"}
                </div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">Scheduled</div>
                <div className="text-sm font-medium">
                  {preview.score.scheduledCount}
                </div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">
                  Unscheduled
                </div>
                <div className="text-sm font-medium">
                  {preview.score.unscheduledCount}
                </div>
              </div>
            </section>

            {/* Assignments */}
            <section>
              <h3 className="mb-2 text-sm font-medium">
                Assignments ({preview.assignments.length})
              </h3>
              <div className="max-h-40 overflow-auto rounded-lg border p-3 text-xs">
                {preview.assignments.length === 0 ? (
                  <div className="text-muted-foreground">
                    No assignments proposed.
                  </div>
                ) : (
                  <table className="w-full">
                    <thead>
                      <tr className="text-left text-muted-foreground">
                        <th className="pb-1">Request</th>
                        <th className="pb-1">Space</th>
                        <th className="pb-1">Start</th>
                        <th className="pb-1">End</th>
                        <th className="pb-1">Days</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.assignments.map((a) => (
                        <tr key={a.requestId}>
                          <td className="py-0.5 truncate max-w-[150px]" title={a.requestName}>
                            {a.requestName}
                          </td>
                          <td className="py-0.5 truncate max-w-[120px]" title={a.spaceName}>
                            {a.spaceName}
                          </td>
                          <td className="py-0.5">{a.start}</td>
                          <td className="py-0.5">{a.end}</td>
                          <td className="py-0.5">{a.durationDays}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </section>

            {/* Unscheduled */}
            {preview.unscheduled.length > 0 && (
              <section>
                <h3 className="mb-2 text-sm font-medium">
                  Unscheduled ({preview.unscheduled.length})
                </h3>
                <div className="max-h-32 overflow-auto rounded-lg border p-3 text-xs">
                  <ul className="space-y-1">
                    {preview.unscheduled.map((entry) => (
                      <li key={entry.requestId}>
                        <span className="font-medium">
                          {entry.requestName}
                        </span>
                        :{" "}
                        {entry.reasonCodes
                          .map((c) => REASON_LABELS[c] ?? c)
                          .join(", ")}
                      </li>
                    ))}
                  </ul>
                </div>
              </section>
            )}

            {/* Diagnostics */}
            {preview.diagnostics.length > 0 && (
              <section>
                <h3 className="mb-2 text-sm font-medium">Diagnostics</h3>
                <div className="rounded-lg border p-3 text-xs text-muted-foreground">
                  <ul className="space-y-0.5">
                    {preview.diagnostics.map((line, idx) => (
                      <li key={idx}>{line}</li>
                    ))}
                  </ul>
                </div>
              </section>
            )}
          </div>
        )}

        {applyError && (
          <div className="flex items-start gap-2 rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
            <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
            <span>{applyError}</span>
          </div>
        )}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="button"
            onClick={onApply}
            disabled={
              !preview || preview.assignments.length === 0 || isApplying
            }
          >
            {isApplying && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
            Apply {preview ? `(${preview.assignments.length})` : ""}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

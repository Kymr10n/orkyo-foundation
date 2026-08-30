import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ScaffoldDialog } from "@foundation/src/components/ui/ScaffoldDialog";
import { ScaleSelect } from "@foundation/src/components/utilization/ScaleSelect";
import { TimeNavigator } from "@foundation/src/components/utilization/TimeNavigator";
import { RequestCalendar } from "@foundation/src/components/utilization/RequestCalendar";
import { scaleToCalendarView } from "@foundation/src/components/utilization/request-calendar-events";
import { getFetchWindow } from "@foundation/src/components/utilization/time-grid-utils";
import { navigateCalendarPeriod } from "@foundation/src/lib/utils/time-navigation";
import { ResourceAssignmentDialog } from "@foundation/src/components/utilization/ResourceAssignmentDialog";
import { ResourceAbsenceEditDialog } from "@foundation/src/components/resources/ResourceAbsenceEditDialog";
import { resourceScheduleEvents } from "@foundation/src/components/resources/resource-schedule-events";
import {
  KIND_SWATCH,
  SEVERITY_SWATCH,
} from "@foundation/src/components/utilization/request-calendar-events";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { getAssignmentsByResource } from "@foundation/src/lib/api/resource-assignments-api";
import { scheduleRequest } from "@foundation/src/lib/api/utilization-api";
import {
  getResourceAbsences,
  updateResourceAbsence,
  type ResourceAbsenceInfo,
} from "@foundation/src/lib/api/resource-absences-api";
import { getRequests } from "@foundation/src/lib/api/request-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import type { TimeScale } from "@foundation/src/components/utilization/ScaleSelect";

// The calendar has three views, not five: scaleToCalendarView collapses year onto the month grid
// and hour onto the day grid, so offering those two would look like the picker did nothing.
const CALENDAR_SCALES: readonly TimeScale[] = ["month", "week", "day"];

// Same vocabulary and the same swatches the Utilization calendar uses, minus the request
// statuses that no event here carries.
const LEGEND = [
  { className: KIND_SWATCH.assignment, label: "Booked" },
  { className: KIND_SWATCH.absence, label: "Absence" },
  { className: SEVERITY_SWATCH.error, label: "Conflicts" },
  { className: SEVERITY_SWATCH.warning, label: "Warnings" },
] as const;

interface ResourceScheduleDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  resourceId: string;
  resourceName: string;
  allocationMode: string;
}

/**
 * One resource's own time: the work booked on it and the periods it is unavailable, on the same
 * calendar the Utilization page uses.
 *
 * Deliberately keeps its own anchor and scale rather than the app store's. The calendar reports
 * every range change through `onDatesSet`, and the page behind this dialog reads that same store —
 * paging here would silently move the board underneath.
 */
export function ResourceScheduleDialog({
  open,
  onOpenChange,
  resourceId,
  resourceName,
  allocationMode,
}: ResourceScheduleDialogProps) {
  const canEdit = useCanEdit();
  const { isPhone } = useBreakpoint();
  const queryClient = useQueryClient();

  const [scale, setScale] = useState<TimeScale>("week");
  const [anchorTs, setAnchorTs] = useState(() => new Date());
  const [slot, setSlot] = useState<{ start: Date; end: Date } | null>(null);
  const [assigning, setAssigning] = useState<{ start: Date; end: Date } | null>(null);
  const [absence, setAbsence] = useState<ResourceAbsenceInfo | "new" | null>(null);

  const window = useMemo(() => getFetchWindow(scale, anchorTs), [scale, anchorTs]);

  const assignments = useQuery({
    queryKey: qk.resources.assignments(resourceId, window.from, window.to),
    queryFn: () => getAssignmentsByResource(resourceId, window.from, window.to),
    staleTime: STALE.OPERATIONAL,
    enabled: open,
  });

  const absences = useQuery({
    queryKey: qk.resources.absences(resourceId),
    queryFn: () => getResourceAbsences(resourceId),
    staleTime: STALE.OPERATIONAL,
    enabled: open,
  });

  // Names only: an assignment carries a request id, and a block labelled by id tells nobody what
  // the resource is doing.
  const requests = useQuery({
    queryKey: qk.requests.list(),
    queryFn: () => getRequests(),
    staleTime: STALE.STANDARD,
    enabled: open,
  });

  // The board colours a block by the conflicts of the request it books; a resource's own
  // calendar has no reason to disagree with it.
  const { conflictsByRequest } = useConflictRegistry();

  const requestsById = useMemo(
    () => new Map((requests.data ?? []).map((r) => [r.id, r])),
    [requests.data],
  );

  const events = useMemo(
    () =>
      resourceScheduleEvents(
        assignments.data ?? [],
        absences.data ?? [],
        requestsById,
        conflictsByRequest,
        canEdit,
      ),
    [assignments.data, absences.data, requestsById, conflictsByRequest, canEdit],
  );

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: qk.resources.absences(resourceId) });
    void queryClient.invalidateQueries({ queryKey: ["resource-assignments", resourceId] });
    invalidateRequestData(queryClient);
  };

  /** Drag and resize are the same operation — a new window for an existing block. */
  const move = async (eventId: string, start: Date, end: Date) => {
    const event = events.find((e) => e.id === eventId);
    if (!event) return;
    try {
      if (event.extendedProps.kind === "absence") {
        const existing = (absences.data ?? []).find((a) => a.id === eventId);
        if (!existing) return;
        await updateResourceAbsence(resourceId, eventId, {
          absenceType: existing.absenceType,
          title: existing.title,
          notes: existing.notes,
          startTs: start.toISOString(),
          endTs: end.toISOString(),
          enabled: existing.enabled,
        });
      } else {
        // Reschedule the work, not the booking row. A booking is where a request sits on this
        // resource; moving only `resource_assignments` leaves `requests.start_ts` behind, and
        // the board and the conflict engine both read the request — so the block would spring
        // back and keep its conflict colour. The same endpoint the board drags through cancels
        // and rewrites this type's assignment inside one transaction.
        const { requestId } = event.extendedProps;
        if (!requestId) return;
        await scheduleRequest(requestId, {
          resourceId,
          startTs: start.toISOString(),
          endTs: end.toISOString(),
        });
      }
      refresh();
    } catch {
      // The calendar has already drawn the block in its new place; the refetch puts it back.
      toast.error("Could not move that. Please try again.");
      refresh();
    }
  };

  const openEvent = (eventId: string) => {
    const event = events.find((e) => e.id === eventId);
    if (event?.extendedProps.kind !== "absence") return;
    const existing = (absences.data ?? []).find((a) => a.id === eventId);
    if (existing) setAbsence(existing);
  };

  return (
    <>
      <ScaffoldDialog
        open={open}
        onOpenChange={onOpenChange}
        size="xl"
        title={`Schedule — ${resourceName}`}
        description="Bookings and absences for this resource."
        srOnlyDescription
      >
        <div className="flex items-center justify-between gap-2 px-6 pb-3">
          <TimeNavigator
            scale={scale}
            anchorTs={anchorTs}
            onAnchorChange={setAnchorTs}
            onPrevious={() => setAnchorTs((a) => navigateCalendarPeriod(a, scale, -1))}
            onNext={() => setAnchorTs((a) => navigateCalendarPeriod(a, scale, 1))}
            onToday={() => setAnchorTs(new Date())}
            compact
          />
          <ScaleSelect value={scale} onChange={setScale} scales={CALENDAR_SCALES} compact />
        </div>
        <div className="flex min-h-0 flex-1 flex-col border-t">
          <RequestCalendar
            events={events}
            editable={canEdit}
            initialView={scaleToCalendarView(scale, { phone: isPhone })}
            initialDate={anchorTs}
            active={open}
            legend={LEGEND}
            // Every event here is an absence or a booking, so a request-status filter would
            // only ever hide everything.
            showFilterBar={false}
            onEventClick={openEvent}
            onEventMove={(id, start, end) => void move(id, start, end)}
            onEventResize={(id, start, end) => void move(id, start, end)}
            onSlotSelect={(start, end) => setSlot({ start, end })}
            onDatesSet={setAnchorTs}
          />
        </div>
      </ScaffoldDialog>

      {slot && (
        <SlotChooser
          onClose={() => setSlot(null)}
          onBlockTime={() => {
            setAbsence("new");
            setSlot(null);
          }}
          onAssign={() => {
            setAssigning(slot);
            setSlot(null);
          }}
        />
      )}

      {assigning && (
        <ResourceAssignmentDialog
          open
          onOpenChange={(next) => !next && setAssigning(null)}
          resourceId={resourceId}
          resourceName={resourceName}
          allocationMode={allocationMode}
          start={assigning.start.toISOString()}
          end={assigning.end.toISOString()}
        />
      )}

      {absence && (
        <ResourceAbsenceEditDialog
          isOpen
          resourceId={resourceId}
          absence={absence === "new" ? undefined : absence}
          onClose={() => setAbsence(null)}
          onSaved={() => {
            setAbsence(null);
            refresh();
          }}
        />
      )}
    </>
  );
}

/** Two things can occupy a free slot, so the click asks which rather than guessing. */
function SlotChooser({
  onClose,
  onBlockTime,
  onAssign,
}: {
  onClose: () => void;
  onBlockTime: () => void;
  onAssign: () => void;
}) {
  return (
    <ScaffoldDialog
      open
      onOpenChange={(next) => !next && onClose()}
      size="sm"
      contentClassName="h-auto max-h-[85dvh]"
      title="Add to this slot"
      description="Choose what to put in the selected time."
      srOnlyDescription
    >
      <div className="flex flex-col gap-2 px-6 pb-6">
        <button
          type="button"
          onClick={onAssign}
          className="rounded-md border p-3 text-left text-sm hover:bg-muted/50"
        >
          <span className="font-medium">Assign a request</span>
          <span className="block text-muted-foreground">Book this resource onto work.</span>
        </button>
        <button
          type="button"
          onClick={onBlockTime}
          className="rounded-md border p-3 text-left text-sm hover:bg-muted/50"
        >
          <span className="font-medium">Block time</span>
          <span className="block text-muted-foreground">
            Record an absence so nothing is scheduled here.
          </span>
        </button>
      </div>
    </ScaffoldDialog>
  );
}

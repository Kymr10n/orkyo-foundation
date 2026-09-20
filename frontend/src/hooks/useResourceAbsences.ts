import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createResourceAbsence,
  getResourceAbsences,
  updateResourceAbsence,
  type AbsenceType,
  type ResourceAbsenceInfo,
} from "@foundation/src/lib/api/resource-absences-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/** The periods one resource is unavailable. */
export const useResourceAbsences = (resourceId: string, enabled: boolean) =>
  useQuery({
    queryKey: qk.resources.absences(resourceId),
    queryFn: () => getResourceAbsences(resourceId),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });

/** What the absence form edits: the type, its reason and the window it covers. */
export interface SaveResourceAbsencePayload {
  absenceType: AbsenceType;
  title: string;
  startTs: string;
  endTs: string;
}

/** Records a new absence, or rewrites the one passed in. */
export const useSaveResourceAbsence = (resourceId: string, absence?: ResourceAbsenceInfo) =>
  useMutation({
    mutationFn: (payload: SaveResourceAbsencePayload) =>
      absence
        ? updateResourceAbsence(resourceId, absence.id, { ...payload, notes: absence.notes, enabled: absence.enabled })
        : createResourceAbsence(resourceId, payload),
    meta: {
      successMessage: absence ? 'Absence updated' : 'Absence added',
      errorMessage: absence ? 'Failed to update absence' : 'Failed to add absence',
      // An absence makes existing bookings on this resource conflict, so the conflict registry
      // and the utilization grid are stale the moment it is saved — not just the absence list.
      invalidates: [
        qk.resources.absences(resourceId),
        qk.conflicts.all(),
        qk.utilization.byResourceAll(),
      ],
    },
  });

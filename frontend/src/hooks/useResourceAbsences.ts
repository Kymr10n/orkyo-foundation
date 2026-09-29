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
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";

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

/** An update carries the absence as it was: the form does not edit its notes or enabled flag. */
export type SaveResourceAbsenceVariables = SaveVariables<
  SaveResourceAbsencePayload,
  { payload: SaveResourceAbsencePayload; previous: ResourceAbsenceInfo }
>;

/** Records a new absence, or rewrites the one the variables name. */
export const useSaveResourceAbsence = (resourceId: string) =>
  useMutation({
    mutationFn: (v: SaveResourceAbsenceVariables) =>
      v.id === null
        ? createResourceAbsence(resourceId, v.data)
        : updateResourceAbsence(resourceId, v.id, {
            ...v.data.payload,
            notes: v.data.previous.notes,
            enabled: v.data.previous.enabled,
          }),
    meta: {
      successMessage: savedMessage('Absence added', 'Absence updated'),
      errorMessage: (variables) =>
        (variables as SaveResourceAbsenceVariables).id === null ? 'Failed to add absence' : 'Failed to update absence',
      // An absence makes existing bookings on this resource conflict, so the conflict registry
      // and the utilization grid are stale the moment it is saved — not just the absence list.
      invalidates: [
        qk.resources.absences(resourceId),
        qk.conflicts.all(),
        qk.utilization.byResourceAll(),
      ],
    },
  });

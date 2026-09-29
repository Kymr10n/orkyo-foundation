import { useMutation, useQuery } from "@tanstack/react-query";
import type { SchedulingSettings } from "@foundation/src/domain/scheduling/types";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";
import {
  getSchedulingSettings,
  upsertSchedulingSettings,
  deleteSchedulingSettings,
} from "@foundation/src/lib/api/scheduling-api";
import {
  getAvailabilityEvents,
  createAvailabilityEvent,
  updateAvailabilityEvent,
  deleteAvailabilityEvent,
  type CreateAvailabilityEventRequest,
  type UpdateAvailabilityEventRequest,
} from "@foundation/src/lib/api/availability-events-api";

// ── Settings hooks ──────────────────────────────────────────────

export function useSchedulingSettings(siteId: string | undefined) {
  return useQuery({
    queryKey: qk.scheduling.settings(siteId!),
    queryFn: () => getSchedulingSettings(siteId!),
    enabled: !!siteId,
    staleTime: STALE.OPERATIONAL,
  });
}

export function useUpsertSchedulingSettings(siteId: string) {
  return useMutation({
    mutationFn: (settings: Omit<SchedulingSettings, "siteId">) =>
      upsertSchedulingSettings(siteId, settings),
    meta: {
      invalidates: [qk.scheduling.settings(siteId), qk.requests.all()],
    },
  });
}

export function useDeleteSchedulingSettings(siteId: string) {
  return useMutation({
    mutationFn: () => deleteSchedulingSettings(siteId),
    meta: {
      invalidates: [qk.scheduling.settings(siteId)],
    },
  });
}

// ── Availability Event hooks ────────────────────────────────────

export function useAvailabilityEvents(siteId: string | undefined) {
  return useQuery({
    queryKey: qk.scheduling.availabilityEvents(siteId!),
    queryFn: () => getAvailabilityEvents(siteId!),
    enabled: !!siteId,
    staleTime: STALE.OPERATIONAL,
  });
}

/** Create (`id: null`) or update an event — the event dialog's save; it shows a failure inline. */
export function useSaveAvailabilityEvent(siteId: string) {
  return useMutation({
    mutationFn: (v: SaveVariables<CreateAvailabilityEventRequest, UpdateAvailabilityEventRequest>) =>
      v.id === null
        ? createAvailabilityEvent(siteId, v.data)
        : updateAvailabilityEvent(siteId, v.id, v.data),
    meta: {
      successMessage: savedMessage('Availability event created', 'Availability event updated'),
      suppressErrorToast: true,
      invalidates: [qk.scheduling.availabilityEvents(siteId)],
    },
  });
}

export function useDeleteAvailabilityEvent(siteId: string) {
  return useMutation({
    mutationFn: (eventId: string) => deleteAvailabilityEvent(siteId, eventId),
    meta: {
      successMessage: 'Availability event deleted',
      errorMessage: 'Failed to delete availability event',
      invalidates: [qk.scheduling.availabilityEvents(siteId)],
    },
  });
}

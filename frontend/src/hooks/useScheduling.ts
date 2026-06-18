import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SchedulingSettings } from "@foundation/src/domain/scheduling/types";
import { qk } from "@foundation/src/lib/api/query-keys";
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
    staleTime: 60_000,
  });
}

export function useUpsertSchedulingSettings(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (settings: Omit<SchedulingSettings, "siteId">) =>
      upsertSchedulingSettings(siteId, settings),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: qk.scheduling.settings(siteId) });
      qc.invalidateQueries({ queryKey: qk.requests.all() });
    },
  });
}

export function useDeleteSchedulingSettings(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => deleteSchedulingSettings(siteId),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.scheduling.settings(siteId) }),
  });
}

// ── Availability Event hooks ────────────────────────────────────

export function useAvailabilityEvents(siteId: string | undefined) {
  return useQuery({
    queryKey: qk.scheduling.availabilityEvents(siteId!),
    queryFn: () => getAvailabilityEvents(siteId!),
    enabled: !!siteId,
    staleTime: 60_000,
  });
}

export function useCreateAvailabilityEvent(siteId: string) {
  return useMutation({
    mutationFn: (request: CreateAvailabilityEventRequest) =>
      createAvailabilityEvent(siteId, request),
    meta: {
      successMessage: 'Availability event created',
      errorMessage: 'Failed to create availability event',
      invalidates: [qk.scheduling.availabilityEvents(siteId)],
    },
  });
}

export function useUpdateAvailabilityEvent(siteId: string) {
  return useMutation({
    mutationFn: ({ eventId, updates }: { eventId: string; updates: UpdateAvailabilityEventRequest }) =>
      updateAvailabilityEvent(siteId, eventId, updates),
    meta: {
      successMessage: 'Availability event updated',
      errorMessage: 'Failed to update availability event',
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

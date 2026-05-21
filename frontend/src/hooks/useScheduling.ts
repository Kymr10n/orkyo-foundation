import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SchedulingSettings } from "@foundation/src/domain/scheduling/types";
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

// ── Query keys ──────────────────────────────────────────────────

const keys = {
  settings: (siteId: string) => ["scheduling-settings", siteId] as const,
  availabilityEvents: (siteId: string) => ["availability-events", siteId] as const,
};

// ── Settings hooks ──────────────────────────────────────────────

export function useSchedulingSettings(siteId: string | undefined) {
  return useQuery({
    queryKey: keys.settings(siteId!),
    queryFn: () => getSchedulingSettings(siteId!),
    enabled: !!siteId,
    staleTime: 0,
  });
}

export function useUpsertSchedulingSettings(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (settings: Omit<SchedulingSettings, "siteId">) =>
      upsertSchedulingSettings(siteId, settings),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.settings(siteId) });
      qc.invalidateQueries({ queryKey: ["requests"] });
    },
  });
}

export function useDeleteSchedulingSettings(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => deleteSchedulingSettings(siteId),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.settings(siteId) }),
  });
}

// ── Availability Event hooks ────────────────────────────────────

export function useAvailabilityEvents(siteId: string | undefined) {
  return useQuery({
    queryKey: keys.availabilityEvents(siteId!),
    queryFn: () => getAvailabilityEvents(siteId!),
    enabled: !!siteId,
    staleTime: 60_000,
  });
}

export function useCreateAvailabilityEvent(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateAvailabilityEventRequest) =>
      createAvailabilityEvent(siteId, request),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.availabilityEvents(siteId) }),
  });
}

export function useUpdateAvailabilityEvent(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ eventId, updates }: { eventId: string; updates: UpdateAvailabilityEventRequest }) =>
      updateAvailabilityEvent(siteId, eventId, updates),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.availabilityEvents(siteId) }),
  });
}

export function useDeleteAvailabilityEvent(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (eventId: string) => deleteAvailabilityEvent(siteId, eventId),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.availabilityEvents(siteId) }),
  });
}

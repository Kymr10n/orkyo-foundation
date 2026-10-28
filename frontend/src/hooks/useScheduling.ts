import { useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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

/**
 * Saves the settings form, which autosaves while it keeps the version it was filled with.
 * Saves run one after another (one scope per site). A save that starts from the same version as
 * the previous one sends the version that save stored instead, so the user's own saves chain and
 * only someone else's change fails with 412. A refill (after a 412 or a reset) brings a new
 * version, and the chain starts over from it.
 */
export function useUpsertSchedulingSettings(siteId: string) {
  const queryClient = useQueryClient();
  const chain = useRef<{ from?: number; to?: number }>({});
  return useMutation({
    scope: { id: `scheduling-settings:${siteId}` },
    mutationFn: ({ settings, version }: SaveSchedulingSettingsVariables) =>
      upsertSchedulingSettings(siteId, settings, chain.current.from === version ? chain.current.to : version),
    onSuccess: (saved, { version }) => {
      chain.current = { from: version, to: saved.version };
      queryClient.setQueryData(qk.scheduling.settings(siteId), saved);
    },
    meta: {
      invalidates: [qk.requests.all()],
    },
  });
}

/** The form's values, and the version the form was filled with. */
export interface SaveSchedulingSettingsVariables {
  settings: Omit<SchedulingSettings, "siteId" | "version">;
  version?: number;
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

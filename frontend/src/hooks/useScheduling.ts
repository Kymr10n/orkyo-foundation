import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SchedulingSettings, OffTimeDefinition } from "@/domain/scheduling/types";
import {
  getSchedulingSettings,
  upsertSchedulingSettings,
  deleteSchedulingSettings,
  getOffTimes,
  createOffTime,
  updateOffTime,
  deleteOffTime,
} from "@/lib/api/scheduling-api";

// ── Query keys ──────────────────────────────────────────────────

const keys = {
  settings: (siteId: string) => ["scheduling-settings", siteId] as const,
  offTimes: (siteId: string) => ["off-times", siteId] as const,
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

// ── Off-times hooks ─────────────────────────────────────────────

export function useOffTimes(siteId: string | undefined) {
  return useQuery({
    queryKey: keys.offTimes(siteId!),
    queryFn: () => getOffTimes(siteId!),
    enabled: !!siteId,
    staleTime: 60_000,
  });
}

export function useCreateOffTime(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (offTime: Omit<OffTimeDefinition, "id" | "siteId">) =>
      createOffTime(siteId, offTime),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.offTimes(siteId) }),
  });
}

export function useUpdateOffTime(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ offTimeId, updates }: {
      offTimeId: string;
      updates: Partial<Omit<OffTimeDefinition, "id" | "siteId">>;
    }) => updateOffTime(siteId, offTimeId, updates),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.offTimes(siteId) }),
  });
}

export function useDeleteOffTime(siteId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (offTimeId: string) => deleteOffTime(siteId, offTimeId),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.offTimes(siteId) }),
  });
}

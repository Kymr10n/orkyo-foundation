import { useMutation } from "@tanstack/react-query";
import {
  previewAutoSchedule,
  applyAutoSchedule,
} from "@/lib/api/auto-schedule-api";
import type {
  AutoSchedulePreviewRequest,
  AutoScheduleApplyRequest,
} from "@/lib/api/auto-schedule-api";
import { useAuth } from "@/contexts/AuthContext";
import { useTenantSettings } from "@/hooks/useTenantSettings";

export function usePreviewAutoSchedule() {
  return useMutation({
    mutationFn: (request: AutoSchedulePreviewRequest) =>
      previewAutoSchedule(request),
  });
}

export function useApplyAutoSchedule() {
  return useMutation({
    mutationFn: (request: AutoScheduleApplyRequest) =>
      applyAutoSchedule(request),
  });
}

/**
 * Returns whether auto-schedule is available for the current tenant.
 * Requires Professional tier or above AND the setting to be enabled by admin.
 */
export function useAutoScheduleAvailable(): boolean {
  const { membership } = useAuth();
  const { data } = useTenantSettings();

  const tier = membership?.tier ?? "Free";
  const isProfessionalOrAbove =
    tier === "Professional" || tier === "Enterprise";

  const autoScheduleSetting = data?.settings.find(
    (s) => s.key === "scheduling.auto_schedule_enabled",
  );
  const isEnabled =
    autoScheduleSetting?.currentValue === "True" ||
    autoScheduleSetting?.currentValue === "true";

  return isProfessionalOrAbove && isEnabled;
}

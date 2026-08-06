import { useMutation } from "@tanstack/react-query";
import {
  previewAutoSchedule,
  applyAutoSchedule,
} from "@foundation/src/lib/api/auto-schedule-api";
import type {
  AutoSchedulePreviewRequest,
  AutoScheduleApplyRequest,
} from "@foundation/src/lib/api/auto-schedule-api";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { PlanCodes, planIncludesPremiumFeatures } from "@foundation/contracts/plans";
import { useTenantSettings } from "@foundation/src/hooks/useTenantSettings";

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
 * Requires a premium plan AND the setting to be enabled by admin.
 *
 * Plan-derived rather than entitlement-driven, unlike the other gated features: auto-schedule
 * has no entitlement row and no server-side gate, so there is nothing for the server to
 * report. See the follow-up noted in docs/authorization.md.
 */
export function useAutoScheduleAvailable(): boolean {
  const { membership } = useAuth();
  const { data } = useTenantSettings();

  const tier = membership?.tier ?? PlanCodes.Free;

  const autoScheduleSetting = data?.settings.find(
    (s) => s.key === "scheduling.auto_schedule_enabled",
  );
  const isEnabled =
    autoScheduleSetting?.currentValue === "True" ||
    autoScheduleSetting?.currentValue === "true";

  return planIncludesPremiumFeatures(tier) && isEnabled;
}

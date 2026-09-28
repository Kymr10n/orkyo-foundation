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
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";

export function usePreviewAutoSchedule() {
  return useMutation({
    mutationFn: (request: AutoSchedulePreviewRequest) =>
      previewAutoSchedule(request),
  });
}

/**
 * What an apply sends: the request itself, plus how many requests the preview placed so the
 * success toast can say so (the response counts assignments, not requests).
 */
export interface ApplyAutoScheduleVariables {
  request: AutoScheduleApplyRequest;
  scheduledCount: number;
}

/**
 * Applies a previewed run. The preview dialog stays open on failure and shows the error
 * itself (a 409 means the preview is stale), so the error toast is suppressed.
 */
export function useApplyAutoSchedule() {
  return useMutation({
    mutationFn: ({ request }: ApplyAutoScheduleVariables) => applyAutoSchedule(request),
    meta: {
      successMessage: (_data, variables) => {
        const { scheduledCount } = variables as ApplyAutoScheduleVariables;
        return scheduledCount > 0
          ? `Scheduled ${scheduledCount} request${scheduledCount === 1 ? "" : "s"}`
          : "Auto-schedule applied";
      },
      suppressErrorToast: true,
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
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

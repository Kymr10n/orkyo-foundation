import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getTenantSettings,
  updateTenantSettings,
  resetTenantSetting,
} from "@foundation/src/lib/api/tenant-settings-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/**
 * Fetch tenant settings.
 * - Omit `tenantSlug` to use the current auth tenant.
 * - Pass a string to target a specific tenant.
 * - Pass `null` for site-level settings (no tenant context).
 */
export function useTenantSettings(tenantSlug?: string | null) {
  // null = site scope (no tenant header), undefined = current tenant, string = specific tenant
  const cacheKey = tenantSlug === null ? "__site__" : (tenantSlug ?? "current");
  return useQuery({
    queryKey: qk.tenantSettings.scope(cacheKey),
    queryFn: () => getTenantSettings(tenantSlug),
    staleTime: STALE.OPERATIONAL,
    enabled: tenantSlug !== "", // disable when slug is empty-string (no selection yet)
  });
}

export function useUpdateTenantSettings(tenantSlug?: string | null) {
  return useMutation({
    mutationFn: (settings: Record<string, string>) =>
      updateTenantSettings(settings, tenantSlug),
    meta: {
      invalidates: [qk.tenantSettings.all()],
    },
  });
}

export function useResetTenantSetting(tenantSlug?: string | null) {
  return useMutation({
    mutationFn: (key: string) => resetTenantSetting(key, tenantSlug),
    meta: {
      invalidates: [qk.tenantSettings.all()],
    },
  });
}

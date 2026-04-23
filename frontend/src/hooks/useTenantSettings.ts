import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getTenantSettings,
  updateTenantSettings,
  resetTenantSetting,
} from "@foundation/src/lib/api/tenant-settings-api";

const TENANT_SETTINGS_QUERY_KEY = ["tenant-settings"] as const;

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
    queryKey: [...TENANT_SETTINGS_QUERY_KEY, cacheKey],
    queryFn: () => getTenantSettings(tenantSlug),
    staleTime: 60 * 1000,
    enabled: tenantSlug !== "", // disable when slug is empty-string (no selection yet)
  });
}

export function useUpdateTenantSettings(tenantSlug?: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: Record<string, string>) =>
      updateTenantSettings(settings, tenantSlug),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TENANT_SETTINGS_QUERY_KEY });
    },
  });
}

export function useResetTenantSetting(tenantSlug?: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => resetTenantSetting(key, tenantSlug),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TENANT_SETTINGS_QUERY_KEY });
    },
  });
}

import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { safeStorage } from "@foundation/src/lib/core/safe-storage";

/**
 * The active organization's slug, remembered for hosts without a tenant subdomain (local dev,
 * single-tenant deployments). The auth machine saves it when a membership is chosen and clears
 * it on sign-out; `api-utils` reads it for the `X-Tenant-Slug` header and clears it when a
 * session or break-glass visit ends.
 */
export const tenantStorage = {
  save(slug: string): void {
    safeStorage.set(STORAGE_KEYS.TENANT_SLUG, slug);
  },
  clear(): void {
    safeStorage.remove(STORAGE_KEYS.TENANT_SLUG);
  },
  slug(): string {
    return safeStorage.get(STORAGE_KEYS.TENANT_SLUG) ?? "";
  },
};

import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface NumericQuota {
  key: string;
  unit: "count" | "bytes";
  limit: number;
  used: number;
  unlimited: boolean;
  percentUsed: number;
}

export interface Entitlement {
  key: string;
  enabled: boolean;
}

export interface TenantQuotasResponse {
  quotas: NumericQuota[];
  entitlements: Entitlement[];
}

export async function getTenantQuotas(): Promise<TenantQuotasResponse> {
  return apiGet<TenantQuotasResponse>(API_PATHS.QUOTAS);
}

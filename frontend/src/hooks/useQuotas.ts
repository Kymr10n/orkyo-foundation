import { useQuery } from "@tanstack/react-query";
import { getTenantQuotas } from "@foundation/src/lib/api/quotas-api";

const QUOTAS_QUERY_KEY = ["tenant-quotas"] as const;

export function useQuotas() {
  return useQuery({
    queryKey: QUOTAS_QUERY_KEY,
    queryFn: getTenantQuotas,
    staleTime: 30 * 1000,
  });
}

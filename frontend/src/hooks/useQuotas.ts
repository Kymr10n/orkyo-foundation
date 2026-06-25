import { useQuery } from "@tanstack/react-query";
import { getTenantQuotas } from "@foundation/src/lib/api/quotas-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

export function useQuotas() {
  return useQuery({
    queryKey: qk.quotas.tenant(),
    queryFn: getTenantQuotas,
    staleTime: STALE.REALTIME,
  });
}

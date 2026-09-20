import { useCallback } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { getFloorplanViewData } from "@foundation/src/lib/api/floorplan-api";
import { qk } from "@foundation/src/lib/api/query-keys";

export function useFloorplanViewData(siteId: string | null, enabled = true) {
  return useQuery({
    queryKey: qk.floorplan.viewData(siteId),
    queryFn: () => getFloorplanViewData(siteId!),
    enabled: !!siteId && enabled,
    // Floorplan images are large and change rarely — keep them fresh and cached far
    // longer than the global default.
    staleTime: 30 * 60 * 1000,
    gcTime: 60 * 60 * 1000,
  });
}

/** Refresh one site's floorplan view data — after an upload replaces the image. */
export function useInvalidateFloorplanViewData(siteId: string): () => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: qk.floorplan.viewData(siteId) }),
    [queryClient, siteId],
  );
}

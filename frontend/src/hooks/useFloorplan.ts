import { useQuery } from "@tanstack/react-query";
import { fetchFloorplanViewData } from "@foundation/src/lib/api/floorplan-api";

export function useFloorplanViewData(siteId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["floorplan-view-data", siteId],
    queryFn: () => fetchFloorplanViewData(siteId!),
    enabled: !!siteId && enabled,
    staleTime: 30 * 60 * 1000,
    gcTime: 60 * 60 * 1000,
    refetchOnWindowFocus: false,
  });
}

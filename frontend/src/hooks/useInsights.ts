import {
  getInsightsOverview,
  getInsightsConflicts,
  getInsightsRequests,
  type InsightsBucket,
} from "@foundation/src/lib/api/insights-api";
import { getCriticalPath } from "@foundation/src/lib/api/request-dependency-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { useQuery } from "@tanstack/react-query";

// Analytics data changes slowly relative to operational data — STALE.ANALYTICS keeps
// the dashboard snappy without hammering the aggregation endpoints.

export function useInsightsOverview(siteId: string | null, from: Date, to: Date) {
  return useQuery({
    queryKey: qk.insights.overview(siteId, from, to),
    queryFn: () => getInsightsOverview(from, to, siteId),
    staleTime: STALE.ANALYTICS,
  });
}

export function useInsightsConflicts(siteId: string | null, from: Date, to: Date, bucket: InsightsBucket) {
  return useQuery({
    queryKey: qk.insights.conflicts(siteId, from, to, bucket),
    queryFn: () => getInsightsConflicts(from, to, bucket, siteId),
    staleTime: STALE.ANALYTICS,
  });
}

/**
 * The critical path is a property of the dependency network, not of the selected period, so it
 * takes no from/to — only the site narrows it.
 */
export function useCriticalPath(siteId: string | null) {
  return useQuery({
    queryKey: qk.requests.criticalPath(siteId),
    queryFn: () => getCriticalPath(siteId),
    staleTime: STALE.ANALYTICS,
  });
}

export function useInsightsRequests(siteId: string | null, from: Date, to: Date, bucket: InsightsBucket) {
  return useQuery({
    queryKey: qk.insights.requests(siteId, from, to, bucket),
    queryFn: () => getInsightsRequests(from, to, bucket, siteId),
    staleTime: STALE.ANALYTICS,
  });
}

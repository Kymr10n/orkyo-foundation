import {
  getInsightsOverview,
  getInsightsConflicts,
  getInsightsRequests,
  getInsightsBottlenecks,
  getInsightsUtilization,
  type InsightsBucket,
} from "@foundation/src/lib/api/insights-api";
import { getCriticalPath } from "@foundation/src/lib/api/request-dependency-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { useQueries, useQuery } from "@tanstack/react-query";

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

/**
 * One utilization series per resource type, in the order the keys are given.
 *
 * `useQueries` rather than one hook call per type: the number of types is runtime data, and this
 * is the hook built for a dynamic list of them. The results array stays index-aligned with
 * `typeKeys`, which is what lets the caller see every answer at once.
 */
export function useInsightsUtilizationByType(
  typeKeys: string[],
  siteId: string | null,
  from: Date,
  to: Date,
  bucket: InsightsBucket,
) {
  return useQueries({
    queries: typeKeys.map((typeKey) => ({
      queryKey: qk.insights.utilization(typeKey, siteId, from, to, bucket),
      queryFn: () => getInsightsUtilization(typeKey, from, to, bucket, siteId),
      staleTime: STALE.ANALYTICS,
    })),
  });
}

/**
 * One bottleneck ranking per resource type, in the order the keys are given.
 *
 * Every type is fetched whichever filter the tab has set: a class ranking is the merge of its
 * types' rankings, so narrowing to one type then reads a result already in cache.
 */
export function useInsightsBottlenecksByType(
  typeKeys: string[],
  siteId: string | null,
  from: Date,
  to: Date,
) {
  return useQueries({
    queries: typeKeys.map((typeKey) => ({
      queryKey: qk.insights.bottlenecks(siteId, from, to, typeKey),
      queryFn: () => getInsightsBottlenecks(from, to, siteId, typeKey),
      staleTime: STALE.ANALYTICS,
    })),
  });
}

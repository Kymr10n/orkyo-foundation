/**
 * API client for the built-in Insights dashboard. Shapes mirror the backend
 * Api.Models.Insights records (camelCase). All endpoints are tenant-scoped server-side;
 * the client only supplies the site dimension and the period/bucket.
 */

import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export type InsightsBucket = 'week' | 'month' | 'quarter' | 'year';
/**
 * A resource-type key. Deliberately a plain string, not a union: resource types are tenant data,
 * so the set is only knowable at runtime — see useResourceTypes.
 */
export type InsightsResourceType = string;

export interface ResourceTypeUtilization {
  resourceTypeKey: string;
  displayName: string;
  /** Plural — these labels name a whole type's utilization ("People utilization"). */
  displayNamePlural: string;
  /** null = no capacity configured for this type in the period (not 0%). */
  percent: number | null;
}

export interface InsightsMetadata {
  calculatedAt: string;
  sourceMode: string;
}

export interface InsightsOverview {
  period: { from: string; to: string };
  siteId: string | null;
  requests: {
    total: number;
    scheduled: number;
    unscheduled: number;
    completed: number;
    cancelled: number;
  };
  conflicts: {
    total: number;
    overbooking: number;
    criteriaMismatch: number;
    resourceUnavailable: number;
    scheduleOutsideAvailability: number;
    missingResource: number;
    /** Successor starting before its predecessor finishes (plus lag). */
    sequenceViolation: number;
  };
  utilization: {
    /** One entry per active resource type, ordered as the API returns them. */
    byResourceType: ResourceTypeUtilization[];
  };
  metadata: InsightsMetadata;
}

export interface UtilizationSeriesPoint {
  bucketStart: string;
  bucketEnd: string;
  totalCapacityMinutes: number;
  usedCapacityMinutes: number;
  availableCapacityMinutes: number;
  utilizationPercent: number | null;
  conflictCount: number;
}

export interface InsightsUtilization {
  resourceType: InsightsResourceType;
  bucket: InsightsBucket;
  series: UtilizationSeriesPoint[];
  /**
   * How many active resources of this type the site holds over the window. Zero says the type has
   * nothing here, which is a different statement from "its capacity is zero" — and the two need
   * different words, or none at all.
   */
  resourceCount: number;
  metadata: InsightsMetadata;
}

export interface ConflictSeriesPoint {
  bucketStart: string;
  bucketEnd: string;
  total: number;
  overbooking: number;
  criteriaMismatch: number;
  resourceUnavailable: number;
  scheduleOutsideAvailability: number;
  missingResource: number;
  /** Successor starting before its predecessor finishes (plus lag). */
  sequenceViolation: number;
}

export interface InsightsConflicts {
  bucket: InsightsBucket;
  series: ConflictSeriesPoint[];
  metadata: InsightsMetadata;
}

export interface RequestSeriesPoint {
  bucketStart: string;
  bucketEnd: string;
  total: number;
  new: number;
  inProgress: number;
  done: number;
  deferred: number;
  cancelled: number;
}

export interface InsightsRequests {
  bucket: InsightsBucket;
  series: RequestSeriesPoint[];
  metadata: InsightsMetadata;
}

function periodParams(from: Date, to: Date, siteId?: string | null): Record<string, string> {
  const params: Record<string, string> = { from: from.toISOString(), to: to.toISOString() };
  if (siteId) params.siteId = siteId;
  return params;
}

export function getInsightsOverview(
  from: Date, to: Date, siteId?: string | null,
): Promise<InsightsOverview> {
  return apiGet<InsightsOverview>(API_PATHS.INSIGHTS.OVERVIEW, { params: periodParams(from, to, siteId) });
}

export function getInsightsUtilization(
  resourceType: InsightsResourceType, from: Date, to: Date, bucket: InsightsBucket, siteId?: string | null,
): Promise<InsightsUtilization> {
  return apiGet<InsightsUtilization>(API_PATHS.INSIGHTS.UTILIZATION, {
    params: { ...periodParams(from, to, siteId), bucket, resourceType },
  });
}

/** One overloaded resource — work booked past what its capacity absorbs. */
export interface BottleneckResource {
  resourceId: string;
  name: string;
  resourceTypeKey: string;
  resourceTypeDisplayName: string;
  /** Minutes booked beyond capacity, summed over the period's days. */
  overbookedMinutes: number;
  capacityMinutes: number;
  /** The worst single day, uncapped — a day at 200% is the point of the list. */
  /**
   * Null when the resource published no capacity at all in the period — a percentage of zero
   * capacity has no meaning, and reporting 0 would read as "not busy".
   */
  peakUtilizationPercent: number | null;
}

export interface InsightsBottlenecks {
  period: { from: string; to: string };
  siteId: string | null;
  items: BottleneckResource[];
  metadata: InsightsMetadata;
}

/**
 * The most overloaded resources in the period, worst first. No bucket parameter: the ranking is
 * measured per day server-side whatever period is asked for, because overbooking is a spike that
 * a coarser bucket averages away.
 */
export function getInsightsBottlenecks(
  from: Date, to: Date, siteId?: string | null, resourceType?: InsightsResourceType,
): Promise<InsightsBottlenecks> {
  return apiGet<InsightsBottlenecks>(API_PATHS.INSIGHTS.BOTTLENECKS, {
    params: {
      ...periodParams(from, to, siteId),
      // Omitted means every type, which ranks them against each other — one busy type then
      // fills the whole list. The tab asks per type so each gets its own ranking.
      ...(resourceType ? { resourceType } : {}),
    },
  });
}

export function getInsightsConflicts(
  from: Date, to: Date, bucket: InsightsBucket, siteId?: string | null,
): Promise<InsightsConflicts> {
  return apiGet<InsightsConflicts>(API_PATHS.INSIGHTS.CONFLICTS, {
    params: { ...periodParams(from, to, siteId), bucket },
  });
}

export function getInsightsRequests(
  from: Date, to: Date, bucket: InsightsBucket, siteId?: string | null,
): Promise<InsightsRequests> {
  return apiGet<InsightsRequests>(API_PATHS.INSIGHTS.REQUESTS, {
    params: { ...periodParams(from, to, siteId), bucket },
  });
}

/**
 * API client for the built-in Insights dashboard. Shapes mirror the backend
 * Api.Models.Insights records (camelCase). All endpoints are tenant-scoped server-side;
 * the client only supplies the site dimension and the period/bucket.
 */

import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export type InsightsBucket = 'week' | 'month' | 'quarter' | 'year';
export type InsightsResourceType = 'space' | 'person' | 'tool';

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
  };
  utilization: {
    spacesPercent: number | null;
    peoplePercent: number | null;
    toolsPercent: number | null;
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
  scheduled: number;
  unscheduled: number;
  completed: number;
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

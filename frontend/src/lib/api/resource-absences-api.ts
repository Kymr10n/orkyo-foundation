/**
 * API client for per-resource absence (off-time) operations.
 * Maps to GET/POST/PUT/DELETE /api/resources/{id}/absences[/{absenceId}].
 */

import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceAbsenceInfo {
  id: string;
  siteId: string;
  title: string;
  type: string;
  appliesToAllResources: boolean;
  resourceIds?: string[];
  startTs: string;
  endTs: string;
  isRecurring: boolean;
  recurrenceRule?: string;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export async function getResourceAbsences(resourceId: string, siteId: string): Promise<ResourceAbsenceInfo[]> {
  return apiGet<ResourceAbsenceInfo[]>(
    `${API_PATHS.resourceAbsences(resourceId)}?siteId=${encodeURIComponent(siteId)}`,
  );
}

export async function createResourceAbsence(
  resourceId: string,
  request: {
    siteId: string;
    title: string;
    type: string;
    startTs: string;
    endTs: string;
    isRecurring?: boolean;
    recurrenceRule?: string;
    enabled?: boolean;
  },
): Promise<ResourceAbsenceInfo> {
  return apiPost<ResourceAbsenceInfo>(API_PATHS.resourceAbsences(resourceId), request);
}

export async function deleteResourceAbsence(resourceId: string, absenceId: string): Promise<void> {
  return apiDelete(API_PATHS.resourceAbsence(resourceId, absenceId));
}

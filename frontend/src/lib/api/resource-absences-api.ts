import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export type AbsenceType = 'vacation' | 'sickness' | 'unavailable' | 'training' | 'maintenance' | 'custom';

export interface ResourceAbsenceInfo {
  id: string;
  resourceId: string;
  absenceType: AbsenceType;
  title: string;
  notes?: string;
  startTs: string;
  endTs: string;
  isRecurring: boolean;
  recurrenceRule?: string;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceAbsenceRequest {
  absenceType: AbsenceType;
  title: string;
  notes?: string;
  startTs: string;
  endTs: string;
  isRecurring?: boolean;
  recurrenceRule?: string;
  enabled?: boolean;
}

export interface UpdateResourceAbsenceRequest {
  absenceType?: AbsenceType;
  title?: string;
  notes?: string;
  startTs?: string;
  endTs?: string;
  isRecurring?: boolean;
  recurrenceRule?: string;
  enabled?: boolean;
}

export async function getResourceAbsences(resourceId: string): Promise<ResourceAbsenceInfo[]> {
  return apiGet<ResourceAbsenceInfo[]>(API_PATHS.resourceAbsences(resourceId));
}

export async function createResourceAbsence(
  resourceId: string,
  request: CreateResourceAbsenceRequest,
): Promise<ResourceAbsenceInfo> {
  return apiPost<ResourceAbsenceInfo>(API_PATHS.resourceAbsences(resourceId), request);
}

export async function updateResourceAbsence(
  resourceId: string,
  absenceId: string,
  request: UpdateResourceAbsenceRequest,
): Promise<ResourceAbsenceInfo> {
  return apiPut<ResourceAbsenceInfo>(API_PATHS.resourceAbsence(resourceId, absenceId), request);
}

export async function deleteResourceAbsence(resourceId: string, absenceId: string): Promise<void> {
  return apiDelete(API_PATHS.resourceAbsence(resourceId, absenceId));
}

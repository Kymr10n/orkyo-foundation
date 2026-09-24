import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { AbsenceType } from './resource-absences-api';

export interface ResourceStatusBooking {
  assignmentId: string;
  requestId: string;
  requestName: string;
  startUtc: string;
  endUtc: string;
}

export interface ResourceStatusInfo {
  resourceId: string;
  name: string;
  resourceTypeKey: string;
  isActive: boolean;
  asOfUtc: string;
  current?: ResourceStatusBooking | null;
  next?: ResourceStatusBooking | null;
  activeAbsence?: { id: string; title: string; absenceType: AbsenceType; endTs: string } | null;
  /** Resource-level conflicts of the bookings in the next `lookAheadDays`. */
  conflictCount: number;
  lookAheadDays: number;
  /** Average daily allocation over the last `utilizationDays`, 0–100. */
  utilizationPercent?: number | null;
  utilizationDays: number;
}

export async function getResourceStatus(resourceId: string): Promise<ResourceStatusInfo> {
  return apiGet<ResourceStatusInfo>(API_PATHS.resourceStatus(resourceId));
}

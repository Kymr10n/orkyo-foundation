import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { AbsenceType } from './resource-absences-api';

export interface ResourceStatusBooking {
  requestName: string;
  startUtc: string;
  endUtc: string;
}

export interface ResourceStatusInfo {
  resourceId: string;
  name: string;
  resourceTypeKey: string;
  isActive: boolean;
  current?: ResourceStatusBooking | null;
  next?: ResourceStatusBooking | null;
  activeAbsence?: { title: string; absenceType: AbsenceType; endTs: string } | null;
  /** Bookings in the next `lookAheadDays` that have a resource-level conflict. */
  conflictCount: number;
  lookAheadDays: number;
  /** Average daily allocation over the last `utilizationDays`, 0–100. */
  utilizationPercent?: number | null;
  utilizationDays: number;
}

export async function getResourceStatus(resourceId: string): Promise<ResourceStatusInfo> {
  return apiGet<ResourceStatusInfo>(API_PATHS.resourceStatus(resourceId));
}

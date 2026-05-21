import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export type AvailabilityEventType = 'public_holiday' | 'shutdown' | 'maintenance' | 'custom';
export type DefaultEffect = 'closed' | 'available';
export type ScopeEffect = 'available' | 'closed';
export type ScopeTargetType = 'resource' | 'resource_group' | 'resource_type';

export interface AvailabilityEventScopeInfo {
  id: string;
  availabilityEventId: string;
  targetType: ScopeTargetType;
  targetId: string;
  effect: ScopeEffect;
}

export interface AvailabilityEventInfo {
  id: string;
  siteId: string;
  title: string;
  description?: string;
  eventType: AvailabilityEventType;
  defaultEffect: DefaultEffect;
  startTs: string;
  endTs: string;
  isRecurring: boolean;
  recurrenceRule?: string;
  enabled: boolean;
  scopes: AvailabilityEventScopeInfo[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateAvailabilityEventRequest {
  title: string;
  description?: string;
  eventType?: AvailabilityEventType;
  defaultEffect?: DefaultEffect;
  startTs: string;
  endTs: string;
  isRecurring?: boolean;
  recurrenceRule?: string;
  enabled?: boolean;
  scopes?: { targetType: ScopeTargetType; targetId: string; effect: ScopeEffect }[];
}

export interface UpdateAvailabilityEventRequest {
  title?: string;
  description?: string;
  eventType?: AvailabilityEventType;
  defaultEffect?: DefaultEffect;
  startTs?: string;
  endTs?: string;
  isRecurring?: boolean;
  recurrenceRule?: string;
  enabled?: boolean;
}

export async function getAvailabilityEvents(siteId: string): Promise<AvailabilityEventInfo[]> {
  return apiGet<AvailabilityEventInfo[]>(API_PATHS.availabilityEvents(siteId));
}

export async function getAvailabilityEventById(siteId: string, eventId: string): Promise<AvailabilityEventInfo> {
  return apiGet<AvailabilityEventInfo>(API_PATHS.availabilityEvent(siteId, eventId));
}

export async function createAvailabilityEvent(
  siteId: string,
  request: CreateAvailabilityEventRequest,
): Promise<AvailabilityEventInfo> {
  return apiPost<AvailabilityEventInfo>(API_PATHS.availabilityEvents(siteId), request);
}

export async function updateAvailabilityEvent(
  siteId: string,
  eventId: string,
  request: UpdateAvailabilityEventRequest,
): Promise<AvailabilityEventInfo> {
  return apiPut<AvailabilityEventInfo>(API_PATHS.availabilityEvent(siteId, eventId), request);
}

export async function deleteAvailabilityEvent(siteId: string, eventId: string): Promise<void> {
  return apiDelete(API_PATHS.availabilityEvent(siteId, eventId));
}

export async function addAvailabilityEventScope(
  siteId: string,
  eventId: string,
  request: { targetType: ScopeTargetType; targetId: string; effect: ScopeEffect },
): Promise<AvailabilityEventScopeInfo> {
  return apiPost<AvailabilityEventScopeInfo>(API_PATHS.availabilityEventScopes(siteId, eventId), request);
}

export async function deleteAvailabilityEventScope(
  siteId: string,
  eventId: string,
  scopeId: string,
): Promise<void> {
  return apiDelete(API_PATHS.availabilityEventScope(siteId, eventId, scopeId));
}

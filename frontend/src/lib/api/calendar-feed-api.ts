/**
 * API client for calendar subscriptions (iCalendar feeds).
 */

import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface CalendarSubscriptionInfo {
  id: string;
  userId: string;
  label?: string | null;
  siteId?: string | null;
  createdAt: string;
  /** When a calendar client last fetched the feed; null means it never has. */
  lastUsedAt?: string | null;
}

export interface CreateCalendarSubscriptionRequest {
  label?: string;
  siteId?: string | null;
}

export interface CalendarSubscriptionCreated {
  id: string;
  /** The only time the URL is readable — it is stored hashed. */
  feedUrl: string;
  label?: string | null;
  siteId?: string | null;
}

export async function getCalendarSubscriptions(): Promise<CalendarSubscriptionInfo[]> {
  return apiGet<CalendarSubscriptionInfo[]>(API_PATHS.calendarSubscriptions);
}

export async function createCalendarSubscription(
  request: CreateCalendarSubscriptionRequest,
): Promise<CalendarSubscriptionCreated> {
  return apiPost<CalendarSubscriptionCreated>(API_PATHS.calendarSubscriptions, request);
}

export async function revokeCalendarSubscription(id: string): Promise<void> {
  return apiDelete(API_PATHS.calendarSubscription(id));
}

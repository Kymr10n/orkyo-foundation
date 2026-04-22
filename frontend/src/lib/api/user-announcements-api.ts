/**
 * User-facing Announcements API client.
 *
 * These endpoints are available to any authenticated user.
 * Used by the Account page Messages tab to display and mark-read announcements.
 */

import { apiGet, apiPost } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

// ============================================================================
// Types
// ============================================================================

export interface UserAnnouncement {
  id: string;
  title: string;
  body: string;
  isImportant: boolean;
  createdAt: string;
  updatedAt: string;
  isRead: boolean;
}

// ============================================================================
// User Announcement Endpoints
// ============================================================================

/** Get all active (non-expired) announcements with read state for the current user. */
export async function getActiveAnnouncements(): Promise<{ announcements: UserAnnouncement[] }> {
  return apiGet<{ announcements: UserAnnouncement[] }>(API_PATHS.ANNOUNCEMENTS);
}

/** Get the count of unread announcements for the current user. */
export async function getUnreadAnnouncementCount(): Promise<{ unreadCount: number }> {
  return apiGet<{ unreadCount: number }>(API_PATHS.ANNOUNCEMENTS_UNREAD_COUNT);
}

/** Mark an announcement as read (sets read_revision to current revision). */
export async function markAnnouncementRead(id: string): Promise<void> {
  await apiPost<void>(API_PATHS.announcementRead(id), {}, { skipJsonParse: true });
}

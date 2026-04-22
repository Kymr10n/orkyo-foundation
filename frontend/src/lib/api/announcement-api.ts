/**
 * Announcements API client for platform-wide announcements management.
 *
 * These endpoints require the site-admin role in Keycloak.
 * Used by the AdminPage Announcements tab for CRUD operations.
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_BASE_URL } from '../core/api-utils';

// ============================================================================
// Types
// ============================================================================

export interface Announcement {
  id: string;
  title: string;
  body: string;
  isImportant: boolean;
  revision: number;
  createdAt: string;
  createdByEmail: string | null;
  updatedAt: string;
  updatedByEmail: string | null;
  expiresAt: string;
  isExpired: boolean;
}

export interface CreateAnnouncementRequest {
  title: string;
  body: string;
  isImportant: boolean;
  retentionDays?: number;
}

export interface UpdateAnnouncementRequest {
  title: string;
  body: string;
  isImportant: boolean;
  expiresAt?: string;
}

// ============================================================================
// Announcement Management
// ============================================================================

const BASE = `${API_BASE_URL}/api/admin/announcements`;

export async function getAnnouncements(
  includeExpired = true
): Promise<{ announcements: Announcement[] }> {
  return apiGet<{ announcements: Announcement[] }>(BASE, {
    params: { includeExpired: String(includeExpired) },
  });
}

export async function createAnnouncement(
  data: CreateAnnouncementRequest
): Promise<Announcement> {
  return apiPost<Announcement>(BASE, data);
}

export async function updateAnnouncement(
  id: string,
  data: UpdateAnnouncementRequest
): Promise<Announcement> {
  return apiPut<Announcement>(`${BASE}/${id}`, data);
}

export async function deleteAnnouncement(id: string): Promise<void> {
  await apiDelete(`${BASE}/${id}`);
}

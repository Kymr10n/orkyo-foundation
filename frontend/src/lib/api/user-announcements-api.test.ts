/**
 * Tests for user-announcements-api.ts
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import {
  getActiveAnnouncements,
  getUnreadAnnouncementCount,
  markAnnouncementRead,
} from './user-announcements-api';

vi.mock('../core/api-client');

const mockAnnouncement = {
  id: 'abc-123',
  title: 'Welcome!',
  body: 'Welcome to the platform.',
  isImportant: false,
  createdAt: '2026-03-01T10:00:00Z',
  updatedAt: '2026-03-01T10:00:00Z',
  isRead: false,
};

describe('user-announcements-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // getActiveAnnouncements
  // -----------------------------------------------------------------------

  describe('getActiveAnnouncements', () => {
    it('should call apiGet with correct URL', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({
        announcements: [mockAnnouncement],
      });

      const result = await getActiveAnnouncements();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ANNOUNCEMENTS);
      expect(result.announcements).toHaveLength(1);
      expect(result.announcements[0].title).toBe('Welcome!');
    });

    it('should return empty array when there are no announcements', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({ announcements: [] });

      const result = await getActiveAnnouncements();

      expect(result.announcements).toHaveLength(0);
    });
  });

  // getUnreadAnnouncementCount
  // -----------------------------------------------------------------------

  describe('getUnreadAnnouncementCount', () => {
    it('should call apiGet with unread-count URL', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({ unreadCount: 3 });

      const result = await getUnreadAnnouncementCount();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ANNOUNCEMENTS_UNREAD_COUNT);
      expect(result.unreadCount).toBe(3);
    });

    it('should return 0 when no unread', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({ unreadCount: 0 });

      const result = await getUnreadAnnouncementCount();

      expect(result.unreadCount).toBe(0);
    });
  });

  // markAnnouncementRead
  // -----------------------------------------------------------------------

  describe('markAnnouncementRead', () => {
    it('should call apiPost with correct URL and skip JSON parse', async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue(undefined as never);

      await markAnnouncementRead('abc-123');

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.announcementRead('abc-123'),
        {},
        { skipJsonParse: true }
      );
    });
  });
});

import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getSchedulingSettings,
  upsertSchedulingSettings,
  deleteSchedulingSettings,
} from './scheduling-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const SITE_ID = 'site-abc';

const mockSettingsWire = {
  id: 'set-1',
  siteId: SITE_ID,
  timeZone: 'Europe/Berlin',
  workingHoursEnabled: true,
  workingDayStart: '08:00:00',
  workingDayEnd: '17:00:00',
  weekendsEnabled: true,
  publicHolidaysEnabled: false,
  publicHolidayRegion: null,
};

describe('scheduling-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Settings ────────────────────────────────────────────────

  describe('getSchedulingSettings', () => {
    it('calls apiGet and maps wire format to domain', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSettingsWire);

      const result = await getSchedulingSettings(SITE_ID);

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.scheduling(SITE_ID));
      expect(result).toEqual({
        siteId: SITE_ID,
        timeZone: 'Europe/Berlin',
        workingHoursEnabled: true,
        workingDayStart: '08:00',  // seconds trimmed
        workingDayEnd: '17:00',    // seconds trimmed
        weekendsEnabled: true,
        publicHolidaysEnabled: false,
        publicHolidayRegion: null,
      });
    });

    it('returns default settings when none are configured', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({
        id: '00000000-0000-0000-0000-000000000000',
        siteId: SITE_ID,
        timeZone: 'UTC',
        workingHoursEnabled: false,
        workingDayStart: '08:00:00',
        workingDayEnd: '17:00:00',
        weekendsEnabled: true,
        publicHolidaysEnabled: false,
        publicHolidayRegion: null,
      });

      const result = await getSchedulingSettings(SITE_ID);

      expect(result).toEqual({
        siteId: SITE_ID,
        timeZone: 'UTC',
        workingHoursEnabled: false,
        workingDayStart: '08:00',
        workingDayEnd: '17:00',
        weekendsEnabled: true,
        publicHolidaysEnabled: false,
        publicHolidayRegion: null,
      });
    });

    it('rethrows errors', async () => {
      vi.mocked(apiClient.apiGet).mockRejectedValue(new Error('500 Internal'));

      await expect(getSchedulingSettings(SITE_ID)).rejects.toThrow('500 Internal');
    });
  });

  describe('upsertSchedulingSettings', () => {
    it('calls apiPut with wire format and maps response', async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockSettingsWire);

      const input = {
        timeZone: 'Europe/Berlin',
        workingHoursEnabled: true,
        workingDayStart: '08:00',
        workingDayEnd: '17:00',
        weekendsEnabled: true,
        publicHolidaysEnabled: false,
        publicHolidayRegion: null,
      };

      const result = await upsertSchedulingSettings(SITE_ID, input);

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.scheduling(SITE_ID),
        input,
      );
      expect(result?.workingDayStart).toBe('08:00');
    });
  });

  describe('deleteSchedulingSettings', () => {
    it('calls apiDelete with correct path', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteSchedulingSettings(SITE_ID);

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.scheduling(SITE_ID));
    });
  });

});

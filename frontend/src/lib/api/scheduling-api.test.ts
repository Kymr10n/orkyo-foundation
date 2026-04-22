import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getSchedulingSettings,
  upsertSchedulingSettings,
  deleteSchedulingSettings,
  getOffTimes,
  createOffTime,
  updateOffTime,
  deleteOffTime,
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

const mockOffTimeWire = {
  id: 'ot-1',
  siteId: SITE_ID,
  title: 'Christmas',
  type: 'holiday' as const,
  appliesToAllSpaces: true,
  spaceIds: null,
  startTs: '2026-12-24T00:00:00.000Z',
  endTs: '2026-12-26T00:00:00.000Z',
  isRecurring: false,
  recurrenceRule: null,
  enabled: true,
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

  // ── Off-Times ───────────────────────────────────────────────

  describe('getOffTimes', () => {
    it('calls apiGet and maps timestamps to epoch ms', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockOffTimeWire]);

      const result = await getOffTimes(SITE_ID);

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.offTimes(SITE_ID));
      expect(result).toHaveLength(1);
      expect(result[0].startMs).toBe(new Date('2026-12-24T00:00:00.000Z').getTime());
      expect(result[0].endMs).toBe(new Date('2026-12-26T00:00:00.000Z').getTime());
      expect(result[0].spaceIds).toEqual([]); // null → []
    });
  });

  describe('createOffTime', () => {
    it('maps epoch ms to ISO timestamps', async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockOffTimeWire);

      const start = new Date('2026-12-24T00:00:00.000Z').getTime();
      const end = new Date('2026-12-26T00:00:00.000Z').getTime();

      await createOffTime(SITE_ID, {
        title: 'Christmas',
        type: 'holiday',
        appliesToAllSpaces: true,
        spaceIds: [],
        startMs: start,
        endMs: end,
        isRecurring: false,
        recurrenceRule: null,
        enabled: true,
      });

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.offTimes(SITE_ID),
        expect.objectContaining({
          title: 'Christmas',
          startTs: '2026-12-24T00:00:00.000Z',
          endTs: '2026-12-26T00:00:00.000Z',
        }),
      );
    });

    it('omits spaceIds when appliesToAllSpaces is true', async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockOffTimeWire);

      await createOffTime(SITE_ID, {
        title: 'Test',
        type: 'custom',
        appliesToAllSpaces: true,
        spaceIds: ['space-1'],
        startMs: Date.now(),
        endMs: Date.now() + 3600_000,
        isRecurring: false,
        recurrenceRule: null,
        enabled: true,
      });

      const sentBody = vi.mocked(apiClient.apiPost).mock.calls[0][1];
      expect(sentBody).not.toHaveProperty('spaceIds');
    });
  });

  describe('updateOffTime', () => {
    it('sends only changed fields', async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockOffTimeWire);

      await updateOffTime(SITE_ID, 'ot-1', { title: 'Updated', enabled: false });

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.offTime(SITE_ID, 'ot-1'),
        { title: 'Updated', enabled: false },
      );
    });

    it('converts epoch ms to ISO when updating times', async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockOffTimeWire);

      const newStart = new Date('2026-12-25T00:00:00.000Z').getTime();
      await updateOffTime(SITE_ID, 'ot-1', { startMs: newStart });

      const sentBody = vi.mocked(apiClient.apiPut).mock.calls[0][1];
      expect(sentBody).toEqual({ startTs: '2026-12-25T00:00:00.000Z' });
    });
  });

  describe('deleteOffTime', () => {
    it('calls apiDelete with correct path', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteOffTime(SITE_ID, 'ot-1');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.offTime(SITE_ID, 'ot-1'));
    });
  });
});

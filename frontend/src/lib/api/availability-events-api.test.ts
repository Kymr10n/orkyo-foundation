import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getAvailabilityEvents,
  getAvailabilityEventById,
  createAvailabilityEvent,
  updateAvailabilityEvent,
  deleteAvailabilityEvent,
  addAvailabilityEventScope,
  deleteAvailabilityEventScope,
} from './availability-events-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const SITE_ID = 'site-1';
const EVENT_ID = 'event-1';
const SCOPE_ID = 'scope-1';

const eventResponse = {
  id: EVENT_ID,
  siteId: SITE_ID,
  title: 'Christmas shutdown',
  eventType: 'shutdown' as const,
  defaultEffect: 'closed' as const,
  startTs: '2026-12-24T00:00:00.000Z',
  endTs: '2026-12-26T00:00:00.000Z',
  isRecurring: false,
  enabled: true,
  scopes: [],
  createdAt: '2026-01-01T00:00:00.000Z',
  updatedAt: '2026-01-01T00:00:00.000Z',
};

describe('availability-events-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('lists availability events for a site', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([eventResponse]);

    const result = await getAvailabilityEvents(SITE_ID);

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.availabilityEvents(SITE_ID));
    expect(result).toEqual([eventResponse]);
  });

  it('gets a single availability event by id', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue(eventResponse);

    const result = await getAvailabilityEventById(SITE_ID, EVENT_ID);

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.availabilityEvent(SITE_ID, EVENT_ID));
    expect(result).toEqual(eventResponse);
  });

  it('creates an availability event', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(eventResponse);
    const request = {
      title: 'Christmas shutdown',
      eventType: 'shutdown' as const,
      defaultEffect: 'closed' as const,
      startTs: '2026-12-24T00:00:00.000Z',
      endTs: '2026-12-26T00:00:00.000Z',
    };

    await createAvailabilityEvent(SITE_ID, request);

    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.availabilityEvents(SITE_ID), request);
  });

  it('updates an availability event', async () => {
    vi.mocked(apiClient.apiPut).mockResolvedValue(eventResponse);
    const request = { title: 'Updated title', enabled: false };

    await updateAvailabilityEvent(SITE_ID, EVENT_ID, request);

    expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.availabilityEvent(SITE_ID, EVENT_ID), request);
  });

  it('deletes an availability event', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await deleteAvailabilityEvent(SITE_ID, EVENT_ID);

    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.availabilityEvent(SITE_ID, EVENT_ID));
  });

  it('adds and deletes availability event scopes', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue({
      id: SCOPE_ID,
      availabilityEventId: EVENT_ID,
      targetType: 'resource',
      targetId: 'resource-1',
      effect: 'available',
    });
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await addAvailabilityEventScope(SITE_ID, EVENT_ID, {
      targetType: 'resource',
      targetId: 'resource-1',
      effect: 'available',
    });
    await deleteAvailabilityEventScope(SITE_ID, EVENT_ID, SCOPE_ID);

    expect(apiClient.apiPost).toHaveBeenCalledWith(
      API_PATHS.availabilityEventScopes(SITE_ID, EVENT_ID),
      { targetType: 'resource', targetId: 'resource-1', effect: 'available' },
    );
    expect(apiClient.apiDelete).toHaveBeenCalledWith(
      API_PATHS.availabilityEventScope(SITE_ID, EVENT_ID, SCOPE_ID),
    );
  });
});

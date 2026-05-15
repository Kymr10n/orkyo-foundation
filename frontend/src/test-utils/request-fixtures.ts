import type { Request, ResourceAssignment, ResourceTypeKey } from '@foundation/src/types/requests';

/**
 * Factory function to create Request test fixtures.
 * Use makeRequest() for default values, or makeRequest({ ...overrides })
 * to customize specific fields.
 */
export function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: `req-${Math.random().toString(36).substring(2, 9)}`,
    name: 'Test Request',
    description: null,
    parentRequestId: null,
    planningMode: 'leaf',
    sortOrder: 0,
    assignments: [],
    icon: null,
    startTs: null,
    endTs: null,
    earliestStartTs: null,
    latestEndTs: null,
    minimalDurationValue: 60,
    minimalDurationUnit: 'minutes',
    actualDurationValue: null,
    actualDurationUnit: null,
    schedulingSettingsApply: true,
    status: 'planned',
    requirements: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    isScheduled: false,
    durationMin: undefined,
    ...overrides,
  };
}

/**
 * Shorthand for a space assignment — replaces the common
 * `{ resourceId, resourceTypeKey: 'space' }` partial literal in tests.
 */
export function spaceAssignment(resourceId: string, overrides: Partial<ResourceAssignment> = {}): ResourceAssignment {
  return makeAssignment(resourceId, 'space', overrides);
}

/**
 * Create a request with a space assignment.
 */
export function makeScheduledRequest(
  spaceId: string,
  startTs: string,
  endTs: string,
  overrides: Partial<Request> = {}
): Request {
  return makeRequest({
    assignments: [spaceAssignment(spaceId, { startUtc: startTs, endUtc: endTs })],
    startTs,
    endTs,
    isScheduled: true,
    ...overrides,
  });
}

/**
 * Create a request with multiple assignments (space + person + tool).
 */
export function makeMultiResourceRequest(
  assignments: { resourceId: string; resourceTypeKey: ResourceTypeKey }[],
  overrides: Partial<Request> = {}
): Request {
  return makeRequest({
    assignments: assignments.map(a => makeAssignment(a.resourceId, a.resourceTypeKey)),
    startTs: '2026-01-01T08:00:00Z',
    endTs: '2026-01-01T10:00:00Z',
    isScheduled: true,
    ...overrides,
  });
}

/**
 * Create a ResourceAssignment fixture.
 */
export function makeAssignment(
  resourceId: string,
  resourceTypeKey: ResourceTypeKey,
  overrides: Partial<ResourceAssignment> = {}
): ResourceAssignment {
  return {
    id: `assign-${Math.random().toString(36).substring(2, 9)}`,
    resourceId,
    resourceTypeKey,
    startUtc: '2026-01-01T08:00:00Z',
    endUtc: '2026-01-01T10:00:00Z',
    allocationPercent: null,
    allocationUnits: null,
    assignmentStatus: 'Planned',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

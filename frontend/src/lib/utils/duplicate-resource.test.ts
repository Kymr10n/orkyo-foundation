import { describe, it, expect } from 'vitest';
import { duplicateResourceRequest } from './duplicate-resource';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

function station(overrides: Partial<ResourceInfo> = {}): ResourceInfo {
  return {
    id: 'station-1',
    resourceTypeId: 'type-space',
    resourceTypeKey: 'space',
    name: 'Bay 3',
    code: 'B-3',
    description: 'Assembly bay',
    externalReference: 'ERP-77',
    allocationMode: 'Exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    homeSiteId: 'site-1',
    crossSiteAllowed: false,
    isPhysical: true,
    capacity: 4,
    geometry: {
      type: 'rectangle',
      coordinates: [
        { x: 100, y: 100 },
        { x: 200, y: 160 },
      ],
    },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('duplicateResourceRequest', () => {
  it('leaves the code behind', () => {
    // Codes are unique per site, so a copy that carried one would be rejected as a conflict —
    // the reason this builder exists rather than being written inline at each call site.
    const request = duplicateResourceRequest(station(), 'site-1');

    expect(request.code).toBeUndefined();
  });

  it('leaves the external reference behind', () => {
    // It identifies the original in someone else's system; a new physical thing has no claim on it.
    const request = duplicateResourceRequest(station(), 'site-1');

    expect(request.externalReference).toBeUndefined();
  });

  it('carries custom field values across', () => {
    // Required fields are enforced at create, so dropping them would make Duplicate fail outright
    // for any tenant that has one.
    const request = duplicateResourceRequest(
      station({ customFields: { serial: 'SN-1', voltage: 400 } }),
      'site-1',
    );

    expect(request.customFields).toEqual({ serial: 'SN-1', voltage: 400 });
  });

  it('offsets the shape so the copy is not hidden under the original', () => {
    const request = duplicateResourceRequest(station(), 'site-1');

    expect(request.geometry?.coordinates).toEqual([
      { x: 120, y: 120 },
      { x: 220, y: 180 },
    ]);
  });

  it('keeps a circle circular', () => {
    // Both stored points shift equally, so the distance between them — the radius — is unchanged.
    const request = duplicateResourceRequest(
      station({
        geometry: {
          type: 'circle',
          coordinates: [
            { x: 200, y: 200 },
            { x: 250, y: 200 },
          ],
        },
      }),
      'site-1',
    );

    const [centre, rim] = request.geometry!.coordinates;
    expect(Math.hypot(rim.x - centre.x, rim.y - centre.y)).toBe(50);
    expect(centre).toEqual({ x: 220, y: 220 });
  });

  it('keeps the type, so duplicating a booth gives a booth', () => {
    const request = duplicateResourceRequest(station({ resourceTypeKey: 'booth' }), 'site-1');

    expect(request.resourceTypeKey).toBe('booth');
  });

  it('marks the copy in its name', () => {
    const request = duplicateResourceRequest(station(), 'site-1');

    expect(request.name).toBe('Bay 3 (copy)');
  });

  it('trims a maximal name so the suffix still fits', () => {
    // Names are capped at 200 server-side; suffixing a name already at the limit would be
    // rejected, which reads to the user as "Duplicate is broken" rather than "your name is long".
    const request = duplicateResourceRequest(station({ name: 'x'.repeat(200) }), 'site-1');

    expect(request.name).toHaveLength(200);
    expect(request.name.endsWith(' (copy)')).toBe(true);
  });

  it('places the copy at the site it is being drawn on', () => {
    const request = duplicateResourceRequest(station({ homeSiteId: 'site-old' }), 'site-new');

    expect(request.homeSiteId).toBe('site-new');
    // A placeable resource never travels; the backend rejects it otherwise.
    expect(request.crossSiteAllowed).toBe(false);
  });
});

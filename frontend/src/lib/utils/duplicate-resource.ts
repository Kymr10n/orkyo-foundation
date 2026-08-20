import type {
  CreateResourceRequest,
  ResourceInfo,
} from '@foundation/src/lib/api/resources-api';

/**
 * How far a copy is offset from its original, in floorplan pixels. Absolute, not zoom-relative,
 * because that is the unit geometry coordinates are in — and for a circle it shifts the centre and
 * the rim equally, so the radius survives.
 */
const DUPLICATE_OFFSET_PX = 20;

/** Leaves room for the suffix rather than letting a maximal name fail validation on save. */
const NAME_MAX_LENGTH = 200;
const COPY_SUFFIX = ' (copy)';

/**
 * Builds the create-request for a copy of a placed resource.
 *
 * A function rather than an inline object because two callers need it — the floorplan's
 * right-click menu and the Stations list row — and because of what it must *not* copy:
 *
 * - `code` is unique per site, so copying it makes the create throw a conflict. The copy has no
 *   code until someone gives it one.
 * - `externalReference` identifies the original in someone else's system; a new physical thing has
 *   no claim on it.
 *
 * `customFields` on the other hand must be copied: required fields are enforced at create, so a
 * copy that dropped them would be rejected for any tenant that has one.
 */
export function duplicateResourceRequest(
  resource: ResourceInfo,
  siteId: string,
): CreateResourceRequest {
  return {
    resourceTypeKey: resource.resourceTypeKey,
    name: copyName(resource.name),
    description: resource.description,
    allocationMode: resource.allocationMode,
    baseAvailabilityPercent: resource.baseAvailabilityPercent,
    homeSiteId: siteId,
    crossSiteAllowed: false,
    isPhysical: resource.isPhysical,
    capacity: resource.capacity,
    customFields: resource.customFields ?? undefined,
    geometry: resource.geometry
      ? {
          ...resource.geometry,
          coordinates: resource.geometry.coordinates.map((point) => ({
            x: point.x + DUPLICATE_OFFSET_PX,
            y: point.y + DUPLICATE_OFFSET_PX,
          })),
        }
      : undefined,
  };
}

function copyName(name: string): string {
  const room = NAME_MAX_LENGTH - COPY_SUFFIX.length;
  return `${name.length > room ? name.slice(0, room) : name}${COPY_SUFFIX}`;
}

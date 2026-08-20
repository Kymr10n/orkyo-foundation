import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

/**
 * The two classes every resource type falls into.
 *
 * A **station** has a fixed location and can be placed on a floorplan. An **asset** is mobile, and
 * availability rather than position is how it gets planned.
 *
 * This is a naming of `resource_types.has_geometry`, not a second piece of state. That flag already
 * carries exactly the station semantics — code, geometry, capacity, owning site — so a parallel
 * column would create a four-state matrix with two invalid states and a permanent sync obligation.
 * Deriving it here keeps one source of truth while letting the UI speak in the words a user reads.
 */
export const RESOURCE_CLASS = {
  STATION: 'station',
  ASSET: 'asset',
} as const;

export type ResourceClass = (typeof RESOURCE_CLASS)[keyof typeof RESOURCE_CLASS];

/** The class of one type. */
export function resourceClassOf(type: Pick<ResourceTypeInfo, 'hasGeometry'>): ResourceClass {
  return type.hasGeometry ? RESOURCE_CLASS.STATION : RESOURCE_CLASS.ASSET;
}

/** The route segment a class lives under: `/stations/…` or `/assets/…`. */
export function classSegment(resourceClass: ResourceClass): string {
  return resourceClass === RESOURCE_CLASS.STATION ? 'stations' : 'assets';
}

/** Where one type's instances live. */
export function typeRoute(
  type: Pick<ResourceTypeInfo, 'hasGeometry' | 'key'>,
  tab: 'instances' | 'groups' | 'lists' = 'instances',
): string {
  return `/${classSegment(resourceClassOf(type))}/${type.key}/${tab}`;
}

/** How each class names itself in navigation and page headings. */
export const CLASS_LABELS: Record<ResourceClass, { singular: string; plural: string }> = {
  [RESOURCE_CLASS.STATION]: { singular: 'Station', plural: 'Stations' },
  [RESOURCE_CLASS.ASSET]: { singular: 'Asset', plural: 'Assets' },
};

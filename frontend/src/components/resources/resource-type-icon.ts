import {
  Armchair,
  Bike,
  Boxes,
  Briefcase,
  Building2,
  Camera,
  Car,
  Cpu,
  Drill,
  Forklift,
  Hammer,
  HardHat,
  Laptop,
  Microscope,
  Monitor,
  Package,
  Plane,
  Printer,
  Projector,
  Server,
  Ship,
  Smartphone,
  Truck,
  Users,
  Warehouse,
  Wrench,
  type LucideIcon,
} from "lucide-react";

/**
 * Icons a tenant may pick for a resource type. Deliberately a curated allow-list rather
 * than a dynamic lookup over all of lucide: the whole library would land in the bundle,
 * and an arbitrary stored string could otherwise resolve to anything.
 *
 * Key order is the order the picker renders them in.
 */
export const RESOURCE_TYPE_ICONS = {
  Boxes,
  Package,
  Warehouse,
  Building2,
  Armchair,
  Users,
  Briefcase,
  HardHat,
  Wrench,
  Hammer,
  Drill,
  Forklift,
  Truck,
  Car,
  Bike,
  Plane,
  Ship,
  Laptop,
  Monitor,
  Server,
  Cpu,
  Printer,
  Projector,
  Camera,
  Microscope,
  Smartphone,
} as const satisfies Record<string, LucideIcon>;

export type ResourceTypeIconName = keyof typeof RESOURCE_TYPE_ICONS;

/** The icon used when a type has none set, or names one this build doesn't know. */
export const DEFAULT_RESOURCE_TYPE_ICON = Boxes;

/**
 * Resolves a stored icon name. Unknown and null names fall back to the default rather
 * than throwing — the stored value is tenant data and the allow-list is a bundle detail,
 * so the two can legitimately drift.
 */
export function resourceTypeIcon(name: string | null | undefined): LucideIcon {
  // hasOwn, not a bare index: an inherited key like "constructor" would otherwise
  // resolve to a truthy non-component and slip past the fallback.
  if (!name || !Object.hasOwn(RESOURCE_TYPE_ICONS, name)) {
    return DEFAULT_RESOURCE_TYPE_ICON;
  }
  return RESOURCE_TYPE_ICONS[name as ResourceTypeIconName];
}

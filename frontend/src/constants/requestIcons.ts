import {
  Calendar,
  CalendarClock,
  Clock,
  Flag,
  ListChecks,
  Target,
  Hammer,
  HardHat,
  Wrench,
  Drill,
  Cog,
  Cable,
  Truck,
  Forklift,
  Container,
  PackageOpen,
  Building2,
  Warehouse,
  Factory,
  Construction,
  Users,
  User,
  MapPin,
  Map,
  Pin,
  AlertTriangle,
  ShieldAlert,
  CircleAlert,
  FileText,
  Sparkles,
  type LucideIcon,
} from "lucide-react";

/**
 * Curated palette of icons that can be attached to a Request to make it
 * easier to spot in lists and trees. The ID is what's stored in the DB;
 * the component is resolved at render time via getRequestIcon().
 *
 * Keep the set small and meaningful — when expanding, add the icon here
 * and in tests; no backend or schema changes required.
 */
export interface RequestIconDefinition {
  id: string;
  label: string;
  group: string;
  component: LucideIcon;
}

export const REQUEST_ICONS: readonly RequestIconDefinition[] = [
  // Planning
  { id: "calendar", label: "Calendar", group: "Planning", component: Calendar },
  { id: "calendar-clock", label: "Schedule", group: "Planning", component: CalendarClock },
  { id: "clock", label: "Clock", group: "Planning", component: Clock },
  { id: "flag", label: "Flag", group: "Planning", component: Flag },
  { id: "target", label: "Target", group: "Planning", component: Target },
  { id: "list-checks", label: "Checklist", group: "Planning", component: ListChecks },

  // Work
  { id: "hammer", label: "Hammer", group: "Work", component: Hammer },
  { id: "hard-hat", label: "Hard hat", group: "Work", component: HardHat },
  { id: "wrench", label: "Wrench", group: "Work", component: Wrench },
  { id: "drill", label: "Drill", group: "Work", component: Drill },
  { id: "cog", label: "Cog", group: "Work", component: Cog },
  { id: "cable", label: "Cable", group: "Work", component: Cable },
  { id: "construction", label: "Construction", group: "Work", component: Construction },

  // Logistics
  { id: "truck", label: "Truck", group: "Logistics", component: Truck },
  { id: "forklift", label: "Forklift", group: "Logistics", component: Forklift },
  { id: "container", label: "Container", group: "Logistics", component: Container },
  { id: "package", label: "Package", group: "Logistics", component: PackageOpen },

  // Places
  { id: "building", label: "Building", group: "Places", component: Building2 },
  { id: "warehouse", label: "Warehouse", group: "Places", component: Warehouse },
  { id: "factory", label: "Factory", group: "Places", component: Factory },
  { id: "map-pin", label: "Map pin", group: "Places", component: MapPin },
  { id: "map", label: "Map", group: "Places", component: Map },
  { id: "pin", label: "Pin", group: "Places", component: Pin },

  // People
  { id: "users", label: "Team", group: "People", component: Users },
  { id: "user", label: "Person", group: "People", component: User },

  // Status
  { id: "alert-triangle", label: "Warning", group: "Status", component: AlertTriangle },
  { id: "shield-alert", label: "Risk", group: "Status", component: ShieldAlert },
  { id: "circle-alert", label: "Issue", group: "Status", component: CircleAlert },
  { id: "file-text", label: "Document", group: "Status", component: FileText },
  { id: "sparkles", label: "Highlight", group: "Status", component: Sparkles },
];

export type RequestIconId = (typeof REQUEST_ICONS)[number]["id"];

/**
 * Resolve a stored icon ID to its lucide-react component.
 * Returns undefined when the value is missing or unknown so callers can fall back.
 */
export function getRequestIcon(id: string | null | undefined): LucideIcon | undefined {
  if (!id) return undefined;
  return REQUEST_ICONS.find((entry) => entry.id === id)?.component;
}

/**
 * Stable list of groups in display order, derived from REQUEST_ICONS.
 */
export const REQUEST_ICON_GROUPS: readonly string[] = Array.from(
  new Set(REQUEST_ICONS.map((entry) => entry.group)),
);

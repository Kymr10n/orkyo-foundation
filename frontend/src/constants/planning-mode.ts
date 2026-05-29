import type { PlanningMode } from "@foundation/src/types/requests";

/**
 * Planning mode wire values, as stored in `requests.planning_mode`.
 * Mirrors the backend `PlanningMode` enum ([JsonStringEnumMemberName]) and the
 * `PlanningMode` union in types/requests.ts (which remains the type source).
 */
export const PLANNING_MODE = {
  LEAF: "leaf",
  SUMMARY: "summary",
  CONTAINER: "container",
} as const satisfies Record<string, PlanningMode>;

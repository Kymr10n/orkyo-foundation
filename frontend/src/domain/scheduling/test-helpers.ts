import type { OffTimeRange } from "./types";

export const TZ = "Europe/Berlin";

export function utc(iso: string): number {
  return new Date(iso).getTime();
}

/**
 * An off-time range, the shape `UtilizationPage` builds from availability events and weekends.
 *
 * Four suites hand-rolled this literal, each slightly differently, and none of them combined a
 * range with a coarse bucket — which is how a month-scale bug reached production. One factory so
 * a test is cheap to write at any scale.
 */
export function makeOffTimeRange(
  startISO: string,
  endISO: string,
  resourceIds: string[] | null = null,
): OffTimeRange {
  return {
    id: `off-${startISO}-${endISO}`,
    title: "Off",
    startMs: new Date(startISO).getTime(),
    endMs: new Date(endISO).getTime(),
    resourceIds,
  };
}

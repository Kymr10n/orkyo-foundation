import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { getUtilizationByResource } from "@foundation/src/lib/api/resource-utilization-api";
import { qk } from "@foundation/src/lib/api/query-keys";

/** The active resources of one type at one site — the options in a request's picker. */
export function useResourceOptions(typeKey: string, siteId: string) {
  return useQuery({
    queryKey: [...qk.resources.byType(typeKey), { siteId: siteId || null }],
    queryFn: () => getResources({ resourceTypeKey: typeKey, isActive: true, siteId: siteId || undefined }),
  });
}

/**
 * How busy each resource of one type already is over the request's window, which drives the
 * availability badges in the picker.
 *
 * Hourly resolution below a fortnight, so a job earlier the same day does not make the whole
 * day read as Busy; daily above it, to keep the payload bounded.
 */
export function useResourceWindowUtilization(
  typeKey: string,
  siteId: string,
  windowStartTs: string | undefined,
  windowEndTs: string | undefined,
) {
  const from = windowStartTs ? new Date(windowStartTs) : undefined;
  const to = windowEndTs ? new Date(windowEndTs) : undefined;
  const hasWindow = !!from && !!to && from < to;
  const granularity =
    hasWindow && to.getTime() - from.getTime() > 14 * 24 * 60 * 60 * 1000 ? "day" : "hour";

  return useQuery({
    // The key is built only when there is a window: the factory stamps the dates with
    // toISOString(), which throws on undefined, and `enabled` gates the fetch but not
    // the key — a request with no schedule would take the whole tab down with it.
    queryKey: hasWindow
      ? qk.utilization.byResource(typeKey, siteId || null, from, to, granularity)
      : [...qk.utilization.byResourceAll(), "no-window", typeKey],
    queryFn: () => getUtilizationByResource(from!, to!, granularity, typeKey, siteId || undefined),
    enabled: hasWindow,
    // The badges are a hint, not a gate: keep the previous answer on screen while the
    // next one loads rather than flickering the whole list empty on every window edit.
    placeholderData: keepPreviousData,
  });
}

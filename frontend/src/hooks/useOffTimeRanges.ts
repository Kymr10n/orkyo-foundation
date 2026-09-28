import { useMemo } from "react";
import { addMonths, startOfMonth } from "date-fns";
import { useAvailabilityEvents, useSchedulingSettings } from "@foundation/src/hooks/useScheduling";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

/**
 * The site's closed periods around `anchorTs`, for the scheduling grids' off-time overlay:
 * every enabled "closed" availability event expanded over its recurrence, plus the weekends
 * when the site does not work them.
 *
 * The expansion window is keyed on the anchor's month, not the raw anchor: edge-scroll moves
 * the anchor ~20×/s, and re-expanding recurrences on every tick made panning churn. Panning
 * within a month reuses the same array; the −1/+13-month slack around the month start still
 * covers every visible window.
 */
export function useOffTimeRanges(siteId: string | null, anchorTs: Date): readonly OffTimeRange[] {
  const { data: schedulingSettings } = useSchedulingSettings(siteId ?? undefined);
  const { data: availabilityEventDefs = [] } = useAvailabilityEvents(siteId ?? undefined);
  const monthAnchorMs = startOfMonth(anchorTs).getTime();

  return useMemo(() => {
    const tz = schedulingSettings?.timeZone ?? "UTC";
    const windowStart = addMonths(monthAnchorMs, -1).getTime();
    const windowEnd = addMonths(monthAnchorMs, 13).getTime();
    const expanded = availabilityEventDefs
      .filter((e) => e.enabled && e.defaultEffect === "closed")
      .flatMap((e) => expandRecurrence(
        {
          id: e.id,
          siteId: e.siteId,
          title: e.title,
          type: "custom" as const,
          appliesToAllSpaces: true,
          resourceIds: [],
          startMs: new Date(e.startTs).getTime(),
          endMs: new Date(e.endTs).getTime(),
          isRecurring: e.isRecurring,
          recurrenceRule: e.recurrenceRule ?? null,
          enabled: e.enabled,
        },
        windowStart, windowEnd, tz,
      ));

    // Weekends as off-time ranges (consistent with manual off-times)
    if (schedulingSettings && !schedulingSettings.weekendsEnabled) {
      expanded.push(...generateWeekendRanges(windowStart, windowEnd));
    }

    return expanded;
  }, [availabilityEventDefs, schedulingSettings, monthAnchorMs]);
}

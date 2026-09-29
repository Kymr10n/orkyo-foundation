import { useMemo } from "react";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeScale } from "./ScaleSelect";
import type { TimeColumn } from "./scheduler-types";
import { enrichColumnsWithOffTime } from "./time-grid-offtime";
import { generateTimeColumns, parseTimeToHour, type WorkingHoursConfig } from "./time-grid-utils";

/**
 * The time columns of a utilization grid: generated for the scale and anchor, weekend- and
 * working-hour-aware, with off-time painted on. Every grid on the Utilization page — Stations,
 * Assets, the Requests canvas — asks this exact question, and each used to answer it with its own
 * copy of the same memo. One copy means the column semantics cannot drift between tabs.
 *
 * `weekendsAreOff` marks weekends as non-working. The working-hours config is built inside the
 * memo, so the dependencies are the raw props and no exhaustive-deps exception is needed.
 */
export function useTimeColumns({
  scale,
  anchorTs,
  weekendsAreOff = false,
  workingHoursEnabled = false,
  workingDayStart = "08:00",
  workingDayEnd = "17:00",
  offTimeRanges = [],
}: {
  scale: TimeScale;
  anchorTs: Date;
  weekendsAreOff?: boolean;
  workingHoursEnabled?: boolean;
  workingDayStart?: string;
  workingDayEnd?: string;
  offTimeRanges?: readonly OffTimeRange[];
}): TimeColumn[] {
  return useMemo(() => {
    const workingHours: WorkingHoursConfig | null = workingHoursEnabled
      ? { enabled: true, start: parseTimeToHour(workingDayStart), end: parseTimeToHour(workingDayEnd) }
      : null;
    return enrichColumnsWithOffTime(
      generateTimeColumns(scale, anchorTs, weekendsAreOff, workingHours),
      offTimeRanges,
    );
  }, [scale, anchorTs, weekendsAreOff, workingHoursEnabled, workingDayStart, workingDayEnd, offTimeRanges]);
}

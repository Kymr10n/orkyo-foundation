import { useEffect, useState } from "react";

/**
 * Wall-clock "now" in epoch milliseconds, refreshed every `intervalMs` (default 30s).
 *
 * For live time-derived UI (the scheduler's "Now" marker and the clock-derived `in_progress` status):
 * a single instance can drive several consumers so they all evaluate against the *same* instant and
 * stay in lockstep as time advances, instead of drifting until the next data refetch.
 */
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}

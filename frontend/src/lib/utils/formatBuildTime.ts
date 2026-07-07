import { formatLocalized, HOUR_CYCLE } from "@foundation/src/lib/formatters";

export function formatBuildTime(iso: string): string {
  return formatLocalized(new Date(iso), {
    year: "numeric",
    month: "long",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "short",
    hourCycle: HOUR_CYCLE,
  });
}

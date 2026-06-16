import {
  StatusBanner,
  StatusIndicator,
  severityDotClass,
} from "@foundation/src/components/ui/status-indicator";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * Tailwind background class for a tab/badge dot, coloured by the worst severity among
 * `conflicts` (error → destructive, warning → amber). Returns null when there are none.
 * Thin wrapper over the shared {@link severityDotClass}.
 */
export function conflictDotClass(conflicts: Conflict[]): string | null {
  return severityDotClass(conflicts);
}

/**
 * Inline icon + tooltip flagging the conflict(s) on a single row (a person, the space, or a
 * requirement) in the Edit Request form. Renders nothing when there are no conflicts. The worst
 * severity drives the icon/colour; the tooltip lists every conflict message.
 */
export function ConflictIndicator({
  conflicts,
  className,
}: {
  conflicts: Conflict[];
  className?: string;
}) {
  return <StatusIndicator items={conflicts} className={className} testId="conflict-indicator" />;
}

/**
 * Summary banner listing every conflict on the request being edited — including request-level
 * ones (e.g. timing) that don't map to a single row. Renders nothing when there are no conflicts.
 */
export function ConflictBanner({ conflicts }: { conflicts: Conflict[] }) {
  return (
    <StatusBanner
      items={conflicts}
      testId="conflict-banner"
      className="mx-6 mb-2"
      title={`${conflicts.length} ${conflicts.length === 1 ? "conflict" : "conflicts"} on this request`}
    />
  );
}

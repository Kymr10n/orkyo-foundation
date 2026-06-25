import type { ResourceGroupInfo } from "@foundation/src/lib/api/resource-groups-api";
import type { ShellGroup } from "./TimelineGridShell";

export interface TimeColumn {
  start: Date;
  end: Date;
  label: string;
  isWeekend?: boolean;
  isOutsideWorkingHours?: boolean;
  /** True when a site-wide off-time range (holiday / closure) covers this column. */
  isGlobalOffTime?: boolean;
}

/**
 * Bucket pre-sorted rows into resource groups for the timeline shell, shared by
 * the Spaces and People grids.
 *
 * Groups are emitted in `displayOrder`; rows whose `getGroupIds` is empty (or
 * resolves to no known group) fall into a trailing "Ungrouped" bucket, which is
 * only appended when it has rows. A row that lists several group ids is placed
 * in the first matching group (in displayOrder), so it appears once.
 *
 * `rows` is taken in caller order — each grid sorts/filters its rows before
 * calling, and that order is preserved within every bucket.
 *
 * @param includeEmpty When false, named groups with no rows are dropped
 *   (Spaces); when true they are kept so users see the group structure
 *   (People). The ungrouped bucket is always omitted when empty.
 */
export function groupRowsByResourceGroup<T>(
  rows: readonly T[],
  groups: readonly ResourceGroupInfo[],
  getGroupIds: (row: T) => readonly string[],
  { includeEmpty }: { includeEmpty: boolean },
): ShellGroup<T>[] {
  const sortedGroups = [...groups].sort(
    (a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0),
  );
  // displayOrder → rank, so a row in several groups lands in the earliest one.
  const rankById = new Map(sortedGroups.map((g, i) => [g.id, i]));

  const rowsByGroupId = new Map<string, T[]>();
  const ungrouped: T[] = [];
  for (const row of rows) {
    let bestId: string | undefined;
    let bestRank = Infinity;
    for (const id of getGroupIds(row)) {
      const rank = rankById.get(id);
      if (rank !== undefined && rank < bestRank) {
        bestRank = rank;
        bestId = id;
      }
    }
    if (bestId === undefined) {
      ungrouped.push(row);
      continue;
    }
    const list = rowsByGroupId.get(bestId);
    if (list) list.push(row);
    else rowsByGroupId.set(bestId, [row]);
  }

  const result: ShellGroup<T>[] = [];
  for (const g of sortedGroups) {
    const groupRows = rowsByGroupId.get(g.id) ?? [];
    if (groupRows.length === 0 && !includeEmpty) continue;
    result.push({ id: g.id, name: g.name, color: g.color, rows: groupRows });
  }
  if (ungrouped.length > 0) {
    result.push({ id: "ungrouped", name: "Ungrouped", rows: ungrouped });
  }
  return result;
}

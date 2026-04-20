import type { Space } from "@/types/space";

export interface TimeColumn {
  start: Date;
  end: Date;
  label: string;
  isWeekend?: boolean;
  isOutsideWorkingHours?: boolean;
}

export interface SpacesByGroup {
  groupId: string; // Use "ungrouped" for spaces without a group
  groupName: string;
  groupColor?: string;
  spaces: Space[];
}

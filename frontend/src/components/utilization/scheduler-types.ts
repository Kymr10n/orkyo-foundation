import type { Space } from "@foundation/src/types/space";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";

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

export interface PeopleByGroup {
  groupId: string; // Use "ungrouped" for people without a group
  groupName: string;
  groupColor?: string;
  people: ResourceInfo[];
}

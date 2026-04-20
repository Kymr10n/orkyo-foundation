import React from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import type { SpacesByGroup } from "./scheduler-types";

export const GroupHeader = React.memo(function GroupHeader({
  group,
  isCollapsed,
  onToggle,
}: {
  group: SpacesByGroup;
  isCollapsed: boolean;
  onToggle: () => void;
}) {
  return (
    <div
      className="flex items-center border-b bg-muted/30 hover:bg-muted/50 cursor-pointer sticky top-0 z-10"
      onClick={onToggle}
    >
      <div className="w-40 flex-shrink-0 p-2 border-r flex items-center gap-2">
        {isCollapsed ? (
          <ChevronRight className="h-4 w-4" />
        ) : (
          <ChevronDown className="h-4 w-4" />
        )}
        {group.groupColor && (
          <div
            className="w-3 h-3 rounded-sm border"
            style={{ backgroundColor: group.groupColor }}
          />
        )}
        <span className="font-semibold text-sm truncate">
          {group.groupName}
        </span>
        <span className="text-xs text-muted-foreground">
          ({group.spaces.length})
        </span>
      </div>
      <div className="flex-1" />
    </div>
  );
});

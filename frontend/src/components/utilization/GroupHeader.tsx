import React from "react";
import { ChevronDown, ChevronRight } from "lucide-react";

export const GroupHeader = React.memo(function GroupHeader({
  groupName,
  groupColor,
  count,
  isCollapsed,
  onToggle,
}: {
  groupName: string;
  groupColor?: string;
  count: number;
  isCollapsed: boolean;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      className="flex w-full items-center border-b bg-muted/30 hover:bg-muted/50 cursor-pointer sticky top-0 z-10 text-left"
      onClick={onToggle}
      aria-expanded={!isCollapsed}
    >
      <span className="w-52 flex-shrink-0 p-2 border-r flex items-center gap-2">
        {isCollapsed ? (
          <ChevronRight className="h-4 w-4" aria-hidden />
        ) : (
          <ChevronDown className="h-4 w-4" aria-hidden />
        )}
        {groupColor && (
          <span
            className="w-3 h-3 rounded-sm border"
            style={{ backgroundColor: groupColor }}
          />
        )}
        <span className="font-semibold text-sm truncate">{groupName}</span>
        <span className="text-xs text-muted-foreground">({count})</span>
      </span>
      <span className="flex-1" />
    </button>
  );
});

import React from "react";
import { useDroppable, useDndContext } from "@dnd-kit/core";
import type { Space } from "@foundation/src/types/space";
import type { Request } from "@foundation/src/types/requests";
import type { TimeColumn } from "./scheduler-types";

export const TimeCell = React.memo(function TimeCell({
  column,
  space,
  timeCursorTs: _timeCursorTs,
  requests: _requests,
  onRequestClick: _onRequestClick,
  isOffTime = false,
}: {
  column: TimeColumn;
  space: Space;
  timeCursorTs: Date;
  requests: Request[];
  onRequestClick: (requestId: string) => void;
  isOffTime?: boolean;
}) {
  const { active } = useDndContext();
  const { isOver, setNodeRef } = useDroppable({
    id: `${space.id}-${column.start.getTime()}`,
    data: { resourceId: space.id, startTs: column.start },
    disabled: active === null,
  });

  // Tint priority: drag-over hint wins, then resource-specific off-time
  // (destructive tint), then generic outside-working-hours (muted tint).
  const bg = isOver
    ? "bg-blue-100 dark:bg-blue-900/20"
    : isOffTime
    ? "bg-destructive/15"
    : column.isOutsideWorkingHours
    ? "bg-muted/80"
    : "";

  return (
    <div
      ref={setNodeRef}
      className={`flex-1 min-w-[60px] border-r ${bg}`}
    />
  );
});

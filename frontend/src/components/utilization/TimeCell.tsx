import React from "react";
import { useDroppable } from "@dnd-kit/core";
import type { Space } from "@/types/space";
import type { Request } from "@/types/requests";
import type { TimeColumn } from "./scheduler-types";

export const TimeCell = React.memo(function TimeCell({
  column,
  space,
  timeCursorTs: _timeCursorTs,
  requests: _requests,
  onRequestClick: _onRequestClick,
}: {
  column: TimeColumn;
  space: Space;
  timeCursorTs: Date;
  requests: Request[];
  onRequestClick: (requestId: string) => void;
}) {
  const { isOver, setNodeRef } = useDroppable({
    id: `${space.id}-${column.start.getTime()}`,
    data: { spaceId: space.id, startTs: column.start },
  });

  return (
    <div
      ref={setNodeRef}
      className={`flex-1 min-w-[60px] border-r ${
        isOver ? "bg-blue-100 dark:bg-blue-900/20" :
        column.isOutsideWorkingHours ? "bg-muted/80" : ""
      }`}
    />
  );
});

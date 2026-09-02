import { useMemo, useState } from "react";
import { Input } from "@foundation/src/components/ui/input";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import { formatScheduledWindow } from "@foundation/src/lib/formatters";
import type { RequestPlanChild } from "@foundation/src/lib/api/request-plan-api";

/**
 * The tasks in a group that no dependency touches yet, as a list beside the canvas.
 *
 * They are not on the canvas because there is nothing to draw for them: a graph of four hundred
 * isolated cards is four hundred rows of scrolling that hide the handful of tasks that ARE
 * sequenced. A list costs one row each and stays readable at any size.
 *
 * Selecting a row puts the task on the canvas and selects it, so the next gesture — dragging a
 * port, or picking a predecessor on the selection bar — is the same one the canvas already offers.
 * The task then stays there whether or not the link is made, because a card that vanished the
 * moment the user changed their mind would be worse than one they have to ignore.
 *
 * There is deliberately no open-the-task gesture here: the row leaves the list on the first click,
 * so a double-click could never land, and "Open task" on the selection bar is one press away.
 */
export function PlanBacklogTray({
  tasks,
  selectedId,
  onSelect,
}: {
  tasks: readonly RequestPlanChild[];
  selectedId: string | null;
  onSelect: (requestId: string) => void;
}) {
  const [filter, setFilter] = useState("");

  const visible = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (!needle) return tasks;
    return tasks.filter((t) => t.name.toLowerCase().includes(needle));
  }, [tasks, filter]);

  return (
    <aside
      className="flex w-64 shrink-0 flex-col border-l"
      aria-label="Unsequenced tasks"
      data-testid="plan-tray"
    >
      <div className="space-y-2 border-b px-3 py-2">
        <h3 className="text-xs font-medium">
          Unsequenced ({tasks.length})
        </h3>
        <Input
          className="h-8"
          type="search"
          aria-label="Filter unsequenced tasks"
          placeholder="Filter…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          data-testid="plan-tray-filter"
        />
      </div>

      {/* A plain scrolling list: these rows are 400 buttons on a real plan, and every extra
          wrapper is 400 more nodes for nothing. */}
      <ul className="min-h-0 flex-1 overflow-y-auto p-2">
        {visible.map((task) => (
          <li key={task.id}>
            <button
              type="button"
              onClick={() => onSelect(task.id)}
              data-testid={`plan-tray-row-${task.id}`}
              className={`mb-1 w-full rounded-md border bg-card px-2 py-1.5 text-left text-sm
                transition hover:brightness-95 focus-visible:outline-none focus-visible:ring-2
                focus-visible:ring-ring
                ${task.id === selectedId ? "border-primary ring-2 ring-primary/40" : ""}`}
            >
              <span className="block truncate font-medium" title={task.name}>
                {task.name}
              </span>
              <span className="block truncate text-[11px] text-muted-foreground">
                {formatScheduledWindow(task.startTs, task.endTs)}
              </span>
              <span className="mt-1 block">
                <RequestStatusBadge status={task.status} />
              </span>
            </button>
          </li>
        ))}

        {visible.length === 0 && (
          <li className="px-1 py-2 text-xs text-muted-foreground">No task matches that filter.</li>
        )}
      </ul>
    </aside>
  );
}

import { memo } from "react";
import { Lock, Link2 } from "lucide-react";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import { Badge } from "@foundation/src/components/ui/badge";
import { predecessorLogicBadge } from "@foundation/src/constants/predecessor-logic";
import { PLAN_NODE_HEIGHT, PLAN_NODE_WIDTH } from "@foundation/src/domain/plan-layout";
import { formatScheduledWindow } from "@foundation/src/lib/formatters";
import type { RequestPlanChild } from "@foundation/src/lib/api/request-plan-api";

/**
 * One task in the plan.
 *
 * An HTML card, not SVG: it carries real buttons and a real focus ring, and the popover that
 * edits its start condition needs a normal DOM anchor. The edges beneath it are SVG, where
 * curves belong.
 */
export const PlanNodeCard = memo(function PlanNodeCard({
  child,
  x,
  y,
  predecessorCount,
  selected,
  highlighted,
  editable,
  onOpen,
  onSelect,
  onStartConnect,
}: {
  child: RequestPlanChild;
  x: number;
  y: number;
  /** Predecessors INSIDE the group plus outside — what the condition is judged against. */
  predecessorCount: number;
  selected: boolean;
  /** Briefly true for the two tasks a link was just drawn between, so the change is findable. */
  highlighted?: boolean;
  editable: boolean;
  onOpen: (requestId: string) => void;
  onSelect: (requestId: string) => void;
  /**
   * Pointer-down on a port: the start of drawing an edge. The port says which way it runs —
   * "after" is the outgoing port on the right, "before" the incoming one on the left — so the
   * direction is decided by what the user grabs rather than guessed from the drop.
   */
  onStartConnect?: (requestId: string, direction: "after" | "before", event: React.PointerEvent) => void;
}) {
  const condition = predecessorLogicBadge(
    child.predecessorLogic,
    child.predecessorLogicK,
    predecessorCount,
  );
  const externalLinks = child.externalPredecessorCount + child.externalSuccessorCount;

  return (
    <div
      // Transition on position, not a jump. A link relayers the graph and several cards move at
      // once; teleporting them makes the user hunt for what changed, which is the whole complaint
      // the tray exists to answer.
      className="absolute transition-[left,top] duration-300 ease-out motion-reduce:transition-none"
      style={{ left: x, top: y, width: PLAN_NODE_WIDTH, height: PLAN_NODE_HEIGHT }}
      data-testid={`plan-node-${child.id}`}
    >
      <button
        type="button"
        onClick={() => onSelect(child.id)}
        onDoubleClick={() => onOpen(child.id)}
        className={`h-full w-full rounded-md border bg-card px-2 py-1.5 text-left shadow-sm transition
          hover:brightness-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
          ${selected ? "border-primary ring-2 ring-primary/40" : ""}
          ${highlighted && !selected ? "border-primary ring-2 ring-primary/60" : ""}
          ${child.canStart ? "" : "opacity-70"}`}
        // The lock is a visual cue; the reason belongs in the accessible name too, or a screen
        // reader user is told only the task's name and never why it is greyed.
        aria-label={
          child.canStart
            ? child.name
            : `${child.name}. Cannot start yet: its predecessors are not done.`
        }
      >
        <div className="flex items-center gap-1">
          {!child.canStart && <Lock className="h-3 w-3 shrink-0 text-muted-foreground" aria-hidden="true" />}
          {/* Every card in a big plan can truncate to the same prefix, so the full name has to
              be reachable without opening the task. */}
          <span className="truncate text-sm font-medium" title={child.name}>
            {child.name}
          </span>
        </div>
        {/* Tasks in one group are often named alike — "Machine precision component" 400 times over
            — and a truncated name then identifies nothing. When each ran is the difference. */}
        <span className="mt-0.5 block truncate text-[11px] text-muted-foreground">
          {formatScheduledWindow(child.startTs, child.endTs)}
        </span>
        <div className="mt-1 flex items-center gap-1">
          <RequestStatusBadge status={child.status} />
          {condition && condition !== "ALL" && (
            <Badge variant="outline" className="px-1 py-0 text-[10px]">
              {condition}
            </Badge>
          )}
          {externalLinks > 0 && (
            <span
              className="ml-auto flex items-center gap-0.5 text-[10px] text-muted-foreground"
              title={`${externalLinks} link${externalLinks === 1 ? "" : "s"} outside this group`}
            >
              <Link2 className="h-3 w-3" aria-hidden="true" />
              {externalLinks}
            </span>
          )}
        </div>
      </button>

      {editable && onStartConnect && (
        // Two ports, not one. With a single outgoing port a user who drags from the task they
        // want to run SECOND gets the dependency backwards, and that task stays on the left
        // where predecessors belong — the drawing is right and the gesture was wrong, which is
        // an impossible thing to tell apart. Grabbing a side now states the intent.
        // Pointer-only affordances, deliberately out of the tab order and hidden from assistive
        // tech: they respond to dragging and to nothing else, so 2 focusable-but-inert stops per
        // task (832 on a 416-task plan) would be noise between the cards that DO work. The
        // keyboard path to the same outcome is "Add predecessor" on the selection bar, which also
        // connects tasks too far apart to drag between.
        <>
          <span
            role="presentation"
            aria-hidden="true"
            title={`Drag to make ${child.name} wait for another task`}
            onPointerDown={(event) => onStartConnect(child.id, "before", event)}
            className="absolute -left-1.5 top-1/2 h-3 w-3 -translate-y-1/2 cursor-crosshair
              rounded-full border border-muted-foreground bg-background hover:scale-125"
            style={{ touchAction: "none" }}
          />
          <span
            role="presentation"
            aria-hidden="true"
            title={`Drag to make another task wait for ${child.name}`}
            onPointerDown={(event) => onStartConnect(child.id, "after", event)}
            className="absolute -right-1.5 top-1/2 h-3 w-3 -translate-y-1/2 cursor-crosshair
              rounded-full border border-primary bg-background hover:scale-125"
            style={{ touchAction: "none" }}
          />
        </>
      )}
    </div>
  );
});

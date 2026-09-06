import { Button } from "@foundation/src/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import { canHaveChildren } from "@foundation/src/domain/request-tree";
import type { Request } from "@foundation/src/types/requests";
import { Edit, Network, Trash2 } from "lucide-react";

interface RequestRowActionsProps {
  request: Request;
  /** These are mutations, so the affordance is gated on this — mirrors
   *  RequestTreeView's `canEdit && …` gate. Viewers get no buttons rather than
   *  a 403. Defaults to true. */
  canEdit?: boolean;
  /** Direct children of this request. Decides whether sequencing is offered:
   *  ordering fewer than two tasks is not an operation. */
  childCount?: number;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  /** Opens the dependency planner for this group. Omit to hide the action. */
  onOpenPlan?: (request: Request) => void;
}

/**
 * Shared per-row actions for a request — used by the tree and list views.
 * Edit + Delete inline icon buttons, and — on a group that has tasks to order —
 * a direct route to the dependency planner. Reparenting lives in the tree drag
 * and the request's Edit dialog → Children tab; adding children lives in that
 * same Children tab. So neither appears here (single path).
 */
export function RequestRowActions({
  request,
  canEdit = true,
  childCount = 0,
  onEdit,
  onDelete,
  onOpenPlan,
}: RequestRowActionsProps) {
  if (!canEdit) return null;

  // The planner orders a parent's children, so it is meaningless on a task and on a group
  // holding one thing. Reaching it used to mean opening the editor, finding the Children tab
  // and closing the editor again — three clicks through a dialog you opened to leave.
  const canSequence =
    onOpenPlan !== undefined && canHaveChildren(request.planningMode) && childCount > 1;

  return (
    <div className="flex items-center justify-end gap-0.5">
      {canSequence && (
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={`Sequence the tasks in ${request.name}`}
              onClick={(e) => { e.stopPropagation(); onOpenPlan(request); }}
            >
              <Network className="h-4 w-4" />
            </Button>
          </TooltipTrigger>
          {/* This one leaves the page, unlike its neighbours, and a graph glyph does not
              say so on its own. */}
          <TooltipContent>Sequence these tasks</TooltipContent>
        </Tooltip>
      )}
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        aria-label={`Edit ${request.name}`}
        onClick={(e) => { e.stopPropagation(); onEdit(request); }}
      >
        <Edit className="h-4 w-4" />
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        className="text-destructive hover:text-destructive"
        aria-label={`Delete ${request.name}`}
        onClick={(e) => { e.stopPropagation(); onDelete(request); }}
      >
        <Trash2 className="h-4 w-4" />
      </Button>
    </div>
  );
}

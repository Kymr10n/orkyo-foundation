import { Button } from "@foundation/src/components/ui/button";
import type { Request } from "@foundation/src/types/requests";
import { Edit, Trash2 } from "lucide-react";

interface RequestRowActionsProps {
  request: Request;
  /** These are mutations, so the affordance is gated on this — mirrors
   *  RequestTreeView's `canEdit && …` gate. Viewers get no buttons rather than
   *  a 403. Defaults to true. */
  canEdit?: boolean;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
}

/**
 * Shared per-row actions for a request — used by the tree and list views.
 * Edit + Delete inline icon buttons. Reparenting lives in the tree drag and the
 * request's Edit dialog → Children tab; adding children lives in that same
 * Children tab. So neither appears here (single path).
 */
export function RequestRowActions({
  request,
  canEdit = true,
  onEdit,
  onDelete,
}: RequestRowActionsProps) {
  if (!canEdit) return null;

  return (
    <div className="flex items-center justify-end gap-0.5">
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

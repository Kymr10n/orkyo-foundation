import { Button } from "@foundation/src/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { Input } from "@foundation/src/components/ui/input";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { getPlanningModeIcon, getPlanningModeLabel } from "@foundation/src/constants";
import {
  canHaveChildren,
  getDescendantIds,
  wouldCreateCycle,
} from "@foundation/src/domain/request-tree";
import type { Request } from "@foundation/src/types/requests";
import { CornerDownRight, Search } from "lucide-react";
import { useMemo, useState } from "react";

interface MoveToDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  request: Request;
  allRequests: Request[];
  onConfirm: (targetParentId: string | null) => void;
}

export function MoveToDialog({
  open,
  onOpenChange,
  request,
  allRequests,
  onConfirm,
}: MoveToDialogProps) {
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const candidates = useMemo(() => {
    const descendantIds = new Set(getDescendantIds(request.id, allRequests));
    return allRequests.filter((r) => {
      if (r.id === request.id) return false;
      if (r.id === request.parentRequestId) return false;
      if (descendantIds.has(r.id)) return false;
      if (!canHaveChildren(r.planningMode)) return false;
      if (wouldCreateCycle(request.id, r.id, allRequests)) return false;
      return true;
    });
  }, [request, allRequests]);

  const filtered = useMemo(() => {
    if (!search) return candidates;
    const q = search.toLowerCase();
    return candidates.filter(
      (r) =>
        r.name.toLowerCase().includes(q) ||
        r.description?.toLowerCase().includes(q),
    );
  }, [candidates, search]);

  const handleConfirm = () => {
    onConfirm(selectedId);
    setSelectedId(null);
    setSearch("");
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Move "{request.name}"</DialogTitle>
          <DialogDescription>
            Select a target parent, or move to root level.
          </DialogDescription>
        </DialogHeader>

        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search targets..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>

        <ScrollArea className="max-h-[300px]">
          <div className="space-y-1 p-1">
            {/* Move to root option */}
            {request.parentRequestId && (
              <button
                type="button"
                className={`w-full flex items-center gap-2 px-3 py-2 rounded-md text-sm hover:bg-accent transition-colors ${
                  selectedId === null ? "bg-accent" : ""
                }`}
                onClick={() => setSelectedId(null)}
              >
                <CornerDownRight className="h-4 w-4 text-muted-foreground" />
                <span className="font-medium">Root level (no parent)</span>
              </button>
            )}

            {filtered.map((r) => {
              const ModeIcon = getPlanningModeIcon(r.planningMode);
              return (
                <button
                  key={r.id}
                  type="button"
                  className={`w-full flex items-center gap-2 px-3 py-2 rounded-md text-sm hover:bg-accent transition-colors ${
                    selectedId === r.id ? "bg-accent ring-1 ring-ring" : ""
                  }`}
                  onClick={() => setSelectedId(r.id)}
                >
                  <ModeIcon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                  <div className="flex flex-col items-start min-w-0">
                    <span className="truncate w-full text-left">{r.name}</span>
                    <span className="text-xs text-muted-foreground">
                      {getPlanningModeLabel(r.planningMode)}
                    </span>
                  </div>
                </button>
              );
            })}

            {filtered.length === 0 && !request.parentRequestId && (
              <p className="text-sm text-muted-foreground py-4 text-center">
                No eligible targets found.
              </p>
            )}
          </div>
        </ScrollArea>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={selectedId === null && !request.parentRequestId}
          >
            Move
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

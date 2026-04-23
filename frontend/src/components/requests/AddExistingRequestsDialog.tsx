import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
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
import { getDescendantIds, wouldCreateCycle } from "@foundation/src/domain/request-tree";
import type { Request } from "@foundation/src/types/requests";
import { Search } from "lucide-react";
import { useCallback, useMemo, useState } from "react";

interface AddExistingRequestsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  parentRequest: Request;
  allRequests: Request[];
  onConfirm: (requestIds: string[]) => void;
}

export function AddExistingRequestsDialog({
  open,
  onOpenChange,
  parentRequest,
  allRequests,
  onConfirm,
}: AddExistingRequestsDialogProps) {
  const [search, setSearch] = useState("");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  // Requests that can be moved under this parent:
  // - Not already a child of this parent
  // - Would not create a cycle
  // - Not the parent itself
  const candidates = useMemo(() => {
    const parentDescendants = new Set(getDescendantIds(parentRequest.id, allRequests));
    return allRequests.filter((r) => {
      if (r.id === parentRequest.id) return false;
      if (r.parentRequestId === parentRequest.id) return false;
      if (parentDescendants.has(r.id)) return false;
      if (wouldCreateCycle(r.id, parentRequest.id, allRequests)) return false;
      return true;
    });
  }, [parentRequest, allRequests]);

  const filtered = useMemo(() => {
    if (!search) return candidates;
    const q = search.toLowerCase();
    return candidates.filter(
      (r) =>
        r.name.toLowerCase().includes(q) ||
        r.description?.toLowerCase().includes(q),
    );
  }, [candidates, search]);

  const toggleSelected = useCallback((id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const handleConfirm = useCallback(() => {
    if (selectedIds.size > 0) {
      onConfirm([...selectedIds]);
      setSelectedIds(new Set());
      setSearch("");
    }
  }, [selectedIds, onConfirm]);

  const handleOpenChange = useCallback(
    (open: boolean) => {
      if (!open) {
        setSelectedIds(new Set());
        setSearch("");
      }
      onOpenChange(open);
    },
    [onOpenChange],
  );

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Add existing requests</DialogTitle>
          <DialogDescription>
            Select requests to move under <strong>{parentRequest.name}</strong>.
            They will become children of this{" "}
            {getPlanningModeLabel(parentRequest.planningMode).toLowerCase()}.
          </DialogDescription>
        </DialogHeader>

        {/* Search */}
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search requests…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>

        {/* List */}
        <ScrollArea className="h-[300px] border rounded-md">
          {filtered.length === 0 ? (
            <div className="flex items-center justify-center h-full text-sm text-muted-foreground p-4">
              {search ? "No matching requests" : "No requests available to add"}
            </div>
          ) : (
            <div className="p-1">
              {filtered.map((r) => {
                const Icon = getPlanningModeIcon(r.planningMode);
                const checked = selectedIds.has(r.id);
                return (
                  <label
                    key={r.id}
                    className={`
                      flex items-center gap-3 px-3 py-2 rounded-md cursor-pointer
                      hover:bg-muted/50 transition-colors
                      ${checked ? "bg-muted" : ""}
                    `}
                  >
                    <Checkbox
                      checked={checked}
                      onCheckedChange={() => toggleSelected(r.id)}
                    />
                    <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                    <span className="text-sm font-medium truncate flex-1">
                      {r.name}
                    </span>
                    <Badge
                      variant="outline"
                      className="text-[10px] px-1.5 py-0 h-5 font-normal flex-shrink-0"
                    >
                      {getPlanningModeLabel(r.planningMode)}
                    </Badge>
                    {r.parentRequestId && (
                      <span className="text-[10px] text-muted-foreground flex-shrink-0">
                        has parent
                      </span>
                    )}
                  </label>
                );
              })}
            </div>
          )}
        </ScrollArea>

        <DialogFooter>
          <div className="flex items-center justify-between w-full">
            <span className="text-sm text-muted-foreground">
              {selectedIds.size} selected
            </span>
            <div className="flex gap-2">
              <Button variant="outline" onClick={() => handleOpenChange(false)}>
                Cancel
              </Button>
              <Button onClick={handleConfirm} disabled={selectedIds.size === 0}>
                Move {selectedIds.size > 0 ? `${selectedIds.size} request${selectedIds.size > 1 ? "s" : ""}` : ""}
              </Button>
            </div>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

import { TabsContent } from "@foundation/src/components/ui/tabs";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { getPlanningModeIcon, getPlanningModeLabel, getRequestIcon } from "@foundation/src/constants";
import type { Request } from "@foundation/src/types/requests";
import { ChevronRight, Link, Plus, Search, Trash2 } from "lucide-react";
import type { RefObject } from "react";
import type { useVirtualizer } from "@tanstack/react-virtual";

interface RequestChildrenSectionProps {
  request: Request | null | undefined;
  readOnly: boolean;
  newChildName: string;
  setNewChildName: (value: string) => void;
  isAddingChild: boolean;
  handleAddChild: () => void | Promise<void>;
  addExistingOpen: boolean;
  setAddExistingOpen: (updater: (prev: boolean) => boolean) => void;
  addExistingSearch: string;
  setAddExistingSearch: (value: string) => void;
  addExistingCandidates: Request[];
  addExistingSelected: Set<string>;
  toggleAddExistingSelected: (id: string) => void;
  addExistingViewportRef: RefObject<HTMLDivElement | null>;
  addExistingVirtualizer: ReturnType<typeof useVirtualizer<HTMLDivElement, Element>>;
  isAddingExisting: boolean;
  handleAddExisting: () => void | Promise<void>;
  pendingChildren: string[];
  pendingExistingRequests: Request[];
  setPendingChildren: (updater: (prev: string[]) => string[]) => void;
  setPendingExistingIds: (updater: (prev: string[]) => string[]) => void;
  directChildren: Request[];
  onNavigate?: (requestId: string) => void;
  handleRemoveChild: (child: Request) => void | Promise<void>;
}

/**
 * CHILDREN tab — groups only, needs the tree. List (both modes) + inline
 * quick-add / remove-from-group (edit mode only).
 */
export function RequestChildrenSection({
  request,
  readOnly,
  newChildName,
  setNewChildName,
  isAddingChild,
  handleAddChild,
  addExistingOpen,
  setAddExistingOpen,
  addExistingSearch,
  setAddExistingSearch,
  addExistingCandidates,
  addExistingSelected,
  toggleAddExistingSelected,
  addExistingViewportRef,
  addExistingVirtualizer,
  isAddingExisting,
  handleAddExisting,
  pendingChildren,
  pendingExistingRequests,
  setPendingChildren,
  setPendingExistingIds,
  directChildren,
  onNavigate,
  handleRemoveChild,
}: RequestChildrenSectionProps) {
  return (
    <TabsContent
      value="children"
      className="mt-0 space-y-3"
      // Children-tab controls (quick-add name, add-existing search /
      // checkboxes) are transient scratch or immediate committed
      // actions — not edits to the request's own fields. Stop their
      // input/change events from bubbling to the form-level
      // setIsDirty handler, so managing children doesn't raise a
      // false "Discard changes?" prompt on close.
      onInput={(e) => e.stopPropagation()}
      onChange={(e) => e.stopPropagation()}
    >
      {/* Quick-add first so it stays visible however long the list gets. */}
      {!readOnly && (
        <div className="flex gap-2">
          <Input
            value={newChildName}
            onChange={(e) => setNewChildName(e.target.value)}
            placeholder="New child name"
            className="flex-1"
            data-testid="new-child-name"
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                void handleAddChild();
              }
            }}
          />
          <Button
            type="button"
            size="sm"
            disabled={!newChildName.trim() || isAddingChild}
            onClick={handleAddChild}
            data-testid="add-child-btn"
          >
            <Plus className="h-4 w-4 mr-1" />
            Add
          </Button>
        </div>
      )}

      {/* Add existing — pull parentless requests into this group.
          Inline (no nested modal). Edit mode moves immediately;
          create mode queues until the group is saved. */}
      {!readOnly && (
        <div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setAddExistingOpen((v) => !v)}
            data-testid="add-existing-toggle"
            aria-expanded={addExistingOpen}
          >
            <Link className="h-4 w-4 mr-1" />
            Add existing
            <ChevronRight
              className={`h-4 w-4 ml-1 transition-transform ${addExistingOpen ? 'rotate-90' : ''}`}
            />
          </Button>

          {addExistingOpen && (
            <div className="mt-2 space-y-2 rounded-md border p-2">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  placeholder="Search requests…"
                  aria-label="Search requests to add"
                  value={addExistingSearch}
                  onChange={(e) => setAddExistingSearch(e.target.value)}
                  className="pl-9"
                />
              </div>

              <ScrollArea type="auto" viewportRef={addExistingViewportRef} className="h-[200px] rounded-md border">
                {addExistingCandidates.length === 0 ? (
                  <div className="flex h-[200px] items-center justify-center p-4 text-sm text-muted-foreground">
                    {addExistingSearch ? "No matching requests" : "No unassigned requests available."}
                  </div>
                ) : (
                  <div
                    className="relative w-full p-1"
                    style={{ height: `${addExistingVirtualizer.getTotalSize()}px` }}
                  >
                    {addExistingVirtualizer.getVirtualItems().map((vi) => {
                      const r = addExistingCandidates[vi.index];
                      const Icon = getPlanningModeIcon(r.planningMode);
                      const checked = addExistingSelected.has(r.id);
                      return (
                        // The wrapping label forwards clicks anywhere on the row to
                        // the checkbox (Radix renders a <button>, which is a
                        // labelable element), so the whole row is the click target.
                        // NOTE: do NOT convert this to a div with onClick — that
                        // triggers an infinite render loop via the Radix ScrollArea
                        // ref (reproduced in Chromium; see requests-dialog-visual
                        // row-click spec).
                        <label
                          key={r.id}
                          className={`absolute left-0 top-0 flex w-full items-center gap-3 rounded-md px-3 py-2 cursor-pointer hover:bg-muted/50 transition-colors ${checked ? 'bg-muted' : ''}`}
                          style={{ height: `${vi.size}px`, transform: `translateY(${vi.start}px)` }}
                        >
                          <Checkbox
                            checked={checked}
                            aria-label={r.name}
                            onCheckedChange={() => toggleAddExistingSelected(r.id)}
                          />
                          <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                          <span className="flex-1 truncate text-sm font-medium">{r.name}</span>
                          <Badge variant="outline" className="text-[10px] flex-shrink-0">
                            {getPlanningModeLabel(r.planningMode)}
                          </Badge>
                        </label>
                      );
                    })}
                  </div>
                )}
              </ScrollArea>

              <div className="flex justify-end">
                <Button
                  type="button"
                  size="sm"
                  disabled={addExistingSelected.size === 0 || isAddingExisting}
                  onClick={handleAddExisting}
                  data-testid="add-existing-confirm"
                >
                  Add {addExistingSelected.size > 0 ? addExistingSelected.size : ''}
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Create mode: locally queued children, created with the group. */}
      {!request ? (
        pendingChildren.length === 0 && pendingExistingRequests.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Child tasks added here are created together with this group.
          </p>
        ) : (
          <div className="space-y-1">
            {pendingChildren.map((childName, index) => {
              const PendingIcon = getPlanningModeIcon('leaf');
              return (
                <div
                  key={`new-${childName}-${index}`}
                  className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
                >
                  <PendingIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                  <span className="flex-1 min-w-0 truncate">{childName}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={`Remove ${childName}`}
                    onClick={() =>
                      setPendingChildren((prev) => prev.filter((_, i) => i !== index))
                    }
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              );
            })}
            {pendingExistingRequests.map((r) => {
              const PendingIcon = getRequestIcon(r.icon) ?? getPlanningModeIcon(r.planningMode);
              return (
                <div
                  key={`existing-${r.id}`}
                  className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
                >
                  <PendingIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                  <span className="flex-1 min-w-0 truncate">{r.name}</span>
                  <Badge variant="outline" className="text-[10px] flex-shrink-0">
                    {getPlanningModeLabel(r.planningMode)}
                  </Badge>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={`Remove ${r.name}`}
                    onClick={() =>
                      setPendingExistingIds((prev) => prev.filter((id) => id !== r.id))
                    }
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              );
            })}
          </div>
        )
      ) : directChildren.length === 0 ? (
        <p className="text-sm text-muted-foreground">No children yet.</p>
      ) : (
        <div className="space-y-1">
          {directChildren.map((child) => {
            const ChildIcon = getRequestIcon(child.icon) ?? getPlanningModeIcon(child.planningMode);
            return (
              <div
                key={child.id}
                className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
              >
                <button
                  type="button"
                  className="flex flex-1 min-w-0 items-center gap-2 text-left"
                  onClick={() => onNavigate?.(child.id)}
                >
                  <ChildIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                  <span className="truncate">{child.name}</span>
                  <Badge variant="outline" className="text-[10px] flex-shrink-0">
                    {getPlanningModeLabel(child.planningMode)}
                  </Badge>
                  <RequestStatusBadge status={child.status} />
                </button>
                {!readOnly && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={`Remove ${child.name} from group`}
                    onClick={() => handleRemoveChild(child)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                )}
              </div>
            );
          })}
        </div>
      )}
    </TabsContent>
  );
}

import { useEffect, useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Input } from "@foundation/src/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { getResources, type ResourceInfo } from "@foundation/src/lib/api/resources-api";
import {
  getResourceGroups,
  getResourceGroupMembers,
  setResourceGroupMembers,
} from "@foundation/src/lib/api/resource-groups-api";
import { logger } from "@foundation/src/lib/core/logger";
import { qk } from "@foundation/src/lib/api/query-keys";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

// Spaces are 1:1 with groups; people and other types may belong to several.
const SPACE_TYPE_KEY = "space";

interface ResourceGroupMembersEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  groupId: string;
  groupName: string;
  resourceTypeKey: string;
  onSuccess?: () => void;
}

export function ResourceGroupMembersEditor({
  open,
  onOpenChange,
  groupId,
  groupName,
  resourceTypeKey,
  onSuccess,
}: ResourceGroupMembersEditorProps) {
  const [allResources, setAllResources] = useState<ResourceInfo[]>([]);
  const [selectedResourceIds, setSelectedResourceIds] = useState<Set<string>>(new Set());
  // For spaces only: resourceId → the OTHER group it currently belongs to (1:1 rule).
  const [otherGroupByResource, setOtherGroupByResource] = useState<Map<string, string>>(new Map());
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Pending move confirmation: spaces that will be moved out of another group on save.
  const [pendingMoves, setPendingMoves] = useState<{ name: string; from: string }[] | null>(null);
  const [search, setSearch] = useState('');
  const [showOnlySelected, setShowOnlySelected] = useState(false);

  const filteredResources = useMemo(() => {
    let result = allResources;
    if (showOnlySelected) result = result.filter((r) => selectedResourceIds.has(r.id));
    if (search.trim()) {
      const q = search.toLowerCase();
      result = result.filter(
        (r) => r.name.toLowerCase().includes(q) ||
               r.externalReference?.toLowerCase().includes(q) ||
               r.description?.toLowerCase().includes(q),
      );
    }
    return result;
  }, [allResources, search, showOnlySelected, selectedResourceIds]);

  const isSpace = resourceTypeKey === SPACE_TYPE_KEY;

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      setError(null);
      setPendingMoves(null);
      setShowOnlySelected(false);
      try {
        const [resourcesRes, membersRes] = await Promise.all([
          getResources({ resourceTypeKey, isActive: true }),
          getResourceGroupMembers(groupId),
        ]);
        if (cancelled) return;
        setAllResources(resourcesRes.data);
        setSelectedResourceIds(new Set(membersRes.members.map((m) => m.id)));

        // Spaces are 1:1 with groups: map each space already in a *different* group so
        // we can warn before moving it. Group count is small, so per-group fetch is cheap.
        if (isSpace) {
          const groups = await getResourceGroups(SPACE_TYPE_KEY);
          const others = groups.filter((g) => g.id !== groupId);
          const memberLists = await Promise.all(others.map((g) => getResourceGroupMembers(g.id)));
          if (cancelled) return;
          const map = new Map<string, string>();
          others.forEach((g, i) => {
            for (const m of memberLists[i].members) map.set(m.id, g.name);
          });
          setOtherGroupByResource(map);
        } else {
          setOtherGroupByResource(new Map());
        }
      } catch (err) {
        if (cancelled) return;
        logger.error("Failed to load resources / group members:", err);
        setError(err instanceof Error ? err.message : "Failed to load");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [open, groupId, resourceTypeKey, isSpace]);

  const handleToggle = (resourceId: string) => {
    const next = new Set(selectedResourceIds);
    if (next.has(resourceId)) next.delete(resourceId);
    else next.add(resourceId);
    setSelectedResourceIds(next);
  };

  const handleSelectAll = () => {
    if (selectedResourceIds.size === allResources.length) {
      setSelectedResourceIds(new Set());
    } else {
      setSelectedResourceIds(new Set(allResources.map((r) => r.id)));
    }
  };

  const handleSave = () => {
    setError(null);
    // For spaces, warn before moving any selected space out of its current group.
    if (isSpace) {
      const moves = Array.from(selectedResourceIds)
        .filter((id) => otherGroupByResource.has(id))
        .map((id) => ({
          name: allResources.find((r) => r.id === id)?.name ?? "space",
          from: otherGroupByResource.get(id)!,
        }));
      if (moves.length > 0) {
        setPendingMoves(moves);
        return;
      }
    }
    commit();
  };

  const saveMutation = useMutation({
    mutationFn: () => setResourceGroupMembers(groupId, Array.from(selectedResourceIds)),
    meta: {
      successMessage: "Members updated",
      errorMessage: "Failed to update members",
      invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    },
    onSuccess: () => {
      setError(null);
      onSuccess?.();
      onOpenChange(false);
    },
    onError: (err) => {
      logger.error("Failed to update group members:", err);
      setError(errorMessage(err));
    },
    onSettled: () => setPendingMoves(null),
  });

  const isSubmitting = saveMutation.isPending;
  const commit = () => saveMutation.mutate();

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[80vh] overflow-hidden">
        <DialogHeader>
          <DialogTitle>Manage Members in &quot;{groupName}&quot;</DialogTitle>
          <DialogDescription className="sr-only">
            Select the resources to include in this group.
          </DialogDescription>
        </DialogHeader>

        {pendingMoves ? (
          <div className="flex-1 space-y-3 py-2">
            <p className="text-sm">
              {pendingMoves.length === 1 ? "This space is" : "These spaces are"} already in another
              group. A space can belong to only one group, so saving will move{" "}
              {pendingMoves.length === 1 ? "it" : "them"} here:
            </p>
            <ScrollArea className="max-h-[40vh] pr-4">
              <ul className="space-y-1">
                {pendingMoves.map((m) => (
                  <li key={m.name} className="text-sm">
                    <span className="font-medium">{m.name}</span>
                    <span className="text-muted-foreground"> — move from “{m.from}” to “{groupName}”</span>
                  </li>
                ))}
              </ul>
            </ScrollArea>
            <ErrorAlert message={error ?? null} />
          </div>
        ) : (
        <>
          {!isLoading && allResources.length > 0 && (
            <Input
              placeholder="Filter by name…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="h-8 text-sm"
            />
          )}
          <ScrollArea className="max-h-[55vh] pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">
                Loading resources…
              </div>
            ) : allResources.length === 0 ? (
              <div className="text-center py-8 border rounded-lg border-dashed">
                <p className="text-sm text-muted-foreground">No resources available.</p>
                <p className="text-xs text-muted-foreground mt-1">
                  Create resources first before assigning them to groups.
                </p>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between pb-2 border-b">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      checked={
                        selectedResourceIds.size === allResources.length && allResources.length > 0
                      }
                      onCheckedChange={handleSelectAll}
                      disabled={isSubmitting}
                    />
                    <span className="text-sm font-medium">Select All</span>
                  </div>
                  <Badge
                    variant={showOnlySelected ? "default" : "outline"}
                    className="cursor-pointer select-none"
                    onClick={() => setShowOnlySelected((v) => !v)}
                  >
                    {selectedResourceIds.size} of {allResources.length} selected
                  </Badge>
                </div>

                <div className="space-y-1">
                  {filteredResources.map((resource) => (
                    <div
                      key={resource.id}
                      className="flex items-center gap-3 p-2 rounded-md hover:bg-muted/50"
                    >
                      <Checkbox
                        checked={selectedResourceIds.has(resource.id)}
                        onCheckedChange={() => handleToggle(resource.id)}
                        disabled={isSubmitting}
                      />
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium truncate">{resource.name}</p>
                        {(resource.externalReference || resource.description) && (
                          <p className="text-xs text-muted-foreground truncate">
                            {resource.externalReference ?? resource.description}
                          </p>
                        )}
                      </div>
                    </div>
                  ))}
                  {filteredResources.length === 0 && (
                    <p className="text-sm text-muted-foreground text-center py-4">
                      No results for "{search}"
                    </p>
                  )}
                </div>
              </>
            )}

            <ErrorAlert message={error ?? null} />
          </div>
        </ScrollArea>
        </>
        )}

        <DialogFooter>
          {pendingMoves ? (
            <>
              <Button
                type="button"
                variant="outline"
                onClick={() => setPendingMoves(null)}
                disabled={isSubmitting}
              >
                Back
              </Button>
              <Button onClick={() => commit()} disabled={isSubmitting}>
                {isSubmitting ? "Saving..." : `Move & Save`}
              </Button>
            </>
          ) : (
            <>
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
                disabled={isSubmitting}
              >
                Cancel
              </Button>
              <Button onClick={handleSave} disabled={isSubmitting || isLoading}>
                {isSubmitting ? "Saving..." : "Save Changes"}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

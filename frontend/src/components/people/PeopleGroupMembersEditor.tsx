import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
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
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { getResources, type ResourceInfo } from "@foundation/src/lib/api/resources-api";
import {
  getResourceGroupMembers,
  setResourceGroupMembers,
} from "@foundation/src/lib/api/resource-groups-api";
import { logger } from "@foundation/src/lib/core/logger";

interface PeopleGroupMembersEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  groupId: string;
  groupName: string;
  onSuccess?: () => void;
}

/**
 * Mirrors GroupSpacesEditor (the established pattern for managing group
 * memberships): checkbox list, Select All + count badge, replace-all save.
 * Restricted to person resources via resourceTypeKey='person'.
 */
export function PeopleGroupMembersEditor({
  open,
  onOpenChange,
  groupId,
  groupName,
  onSuccess,
}: PeopleGroupMembersEditorProps) {
  const [allPeople, setAllPeople] = useState<ResourceInfo[]>([]);
  const [selectedResourceIds, setSelectedResourceIds] = useState<Set<string>>(new Set());
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  // Load all active people + the group's current members on open.
  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [peopleRes, membersRes] = await Promise.all([
          getResources({ resourceTypeKey: "person", isActive: true }),
          getResourceGroupMembers(groupId),
        ]);
        if (cancelled) return;
        setAllPeople(peopleRes.data);
        setSelectedResourceIds(new Set(membersRes.members.map((m) => m.id)));
      } catch (err) {
        if (cancelled) return;
        logger.error("Failed to load people / group members:", err);
        setError(err instanceof Error ? err.message : "Failed to load");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [open, groupId]);

  const handleToggle = (resourceId: string) => {
    const next = new Set(selectedResourceIds);
    if (next.has(resourceId)) next.delete(resourceId);
    else next.add(resourceId);
    setSelectedResourceIds(next);
  };

  const handleSelectAll = () => {
    if (selectedResourceIds.size === allPeople.length) {
      setSelectedResourceIds(new Set());
    } else {
      setSelectedResourceIds(new Set(allPeople.map((p) => p.id)));
    }
  };

  const handleSave = async () => {
    setIsSubmitting(true);
    setError(null);
    try {
      await setResourceGroupMembers(groupId, Array.from(selectedResourceIds));
      // Member count is on the group row; refresh the group list so it updates.
      queryClient.invalidateQueries({ queryKey: ["resource-groups", "person"] });
      onSuccess?.();
      onOpenChange(false);
    } catch (err) {
      logger.error("Failed to update group members:", err);
      setError(err instanceof Error ? err.message : "Failed to update members");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[80vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Manage Members in &quot;{groupName}&quot;</DialogTitle>
          <DialogDescription className="sr-only">
            Select the people to include in this group.
          </DialogDescription>
        </DialogHeader>

        <ScrollArea className="flex-1 pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">
                Loading people...
              </div>
            ) : allPeople.length === 0 ? (
              <div className="text-center py-8 border rounded-lg border-dashed">
                <p className="text-sm text-muted-foreground">No people available.</p>
                <p className="text-xs text-muted-foreground mt-1">
                  Create people first before assigning them to groups.
                </p>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between pb-2 border-b">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      checked={
                        selectedResourceIds.size === allPeople.length && allPeople.length > 0
                      }
                      onCheckedChange={handleSelectAll}
                      disabled={isSubmitting}
                    />
                    <span className="text-sm font-medium">Select All</span>
                  </div>
                  <Badge variant="outline">
                    {selectedResourceIds.size} of {allPeople.length} selected
                  </Badge>
                </div>

                <div className="space-y-1">
                  {allPeople.map((person) => (
                    <div
                      key={person.id}
                      className="flex items-center gap-3 p-2 rounded-md hover:bg-muted/50"
                    >
                      <Checkbox
                        checked={selectedResourceIds.has(person.id)}
                        onCheckedChange={() => handleToggle(person.id)}
                        disabled={isSubmitting}
                      />
                      <div className="flex-1">
                        <p className="text-sm font-medium">{person.name}</p>
                        {person.description && (
                          <p className="text-xs text-muted-foreground">
                            {person.description}
                          </p>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </>
            )}

            {error && (
              <div className="text-sm text-destructive bg-destructive/10 p-3 rounded-md">
                {error}
              </div>
            )}
          </div>
        </ScrollArea>

        <DialogFooter>
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
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

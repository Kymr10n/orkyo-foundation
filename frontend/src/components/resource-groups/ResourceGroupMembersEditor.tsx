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
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [resourcesRes, membersRes] = await Promise.all([
          getResources({ resourceTypeKey, isActive: true }),
          getResourceGroupMembers(groupId),
        ]);
        if (cancelled) return;
        setAllResources(resourcesRes.data);
        setSelectedResourceIds(new Set(membersRes.members.map((m) => m.id)));
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
  }, [open, groupId, resourceTypeKey]);

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

  const handleSave = async () => {
    setIsSubmitting(true);
    setError(null);
    try {
      await setResourceGroupMembers(groupId, Array.from(selectedResourceIds));
      queryClient.invalidateQueries({ queryKey: ["resource-groups", resourceTypeKey] });
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
            Select the resources to include in this group.
          </DialogDescription>
        </DialogHeader>

        <ScrollArea className="flex-1 pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">
                Loading resources...
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
                  <Badge variant="outline">
                    {selectedResourceIds.size} of {allResources.length} selected
                  </Badge>
                </div>

                <div className="space-y-1">
                  {allResources.map((resource) => (
                    <div
                      key={resource.id}
                      className="flex items-center gap-3 p-2 rounded-md hover:bg-muted/50"
                    >
                      <Checkbox
                        checked={selectedResourceIds.has(resource.id)}
                        onCheckedChange={() => handleToggle(resource.id)}
                        disabled={isSubmitting}
                      />
                      <div className="flex-1">
                        <p className="text-sm font-medium">{resource.name}</p>
                        {resource.description && (
                          <p className="text-xs text-muted-foreground">
                            {resource.description}
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

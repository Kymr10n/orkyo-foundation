import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
    Dialog,
    DialogContent,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from "@/components/ui/dialog";
import { ScrollArea } from "@/components/ui/scroll-area";
import type { Site } from "@/lib/api/site-api";
import { getSites } from "@/lib/api/site-api";
import { getSpaces, updateSpace } from "@/lib/api/space-api";
import type { Space } from "@/types/space";
import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { logger } from "@/lib/core/logger";

interface GroupSpacesEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  groupId: string;
  groupName: string;
  onSuccess?: () => void;
}

export function GroupSpacesEditor({
  open,
  onOpenChange,
  groupId,
  groupName,
  onSuccess,
}: GroupSpacesEditorProps) {
  const [allSpaces, setAllSpaces] = useState<Space[]>([]);
  const [sites, setSites] = useState<Site[]>([]);
  const [selectedSpaceIds, setSelectedSpaceIds] = useState(new Set());
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  // Load all spaces from all sites
  useEffect(() => {
    const loadSpaces = async () => {
      if (!open) return;

      setIsLoading(true);
      setError(null);
      try {
        // Get all sites first
        const sitesData = await getSites();
        setSites(sitesData);

        // Fetch spaces from all sites
        const spacesPromises = sitesData.map((site) => getSpaces(site.id));
        const spacesArrays = await Promise.all(spacesPromises);
        const allSpacesData = spacesArrays.flat();

        setAllSpaces(allSpacesData);

        // Pre-select spaces that are already in this group
        const inGroupIds = allSpacesData
          .filter((space) => space.groupId === groupId)
          .map((space) => space.id);
        setSelectedSpaceIds(new Set(inGroupIds));
      } catch (err) {
        logger.error("Failed to load spaces:", err);
        setError(err instanceof Error ? err.message : "Failed to load spaces");
      } finally {
        setIsLoading(false);
      }
    };

    loadSpaces();
  }, [open, groupId]);

  const handleToggleSpace = (spaceId: string) => {
    const newSelected = new Set(selectedSpaceIds);
    if (newSelected.has(spaceId)) {
      newSelected.delete(spaceId);
    } else {
      newSelected.add(spaceId);
    }
    setSelectedSpaceIds(newSelected);
  };

  const handleSelectAll = () => {
    if (selectedSpaceIds.size === allSpaces.length) {
      setSelectedSpaceIds(new Set());
    } else {
      setSelectedSpaceIds(new Set(allSpaces.map((s) => s.id)));
    }
  };

  const handleSave = async () => {
    setIsSubmitting(true);
    setError(null);

    try {
      // Update each space with the new group assignment
      const updates = allSpaces.map(async (space) => {
        const shouldBeInGroup = selectedSpaceIds.has(space.id);
        const isInGroup = space.groupId === groupId;

        if (shouldBeInGroup && !isInGroup) {
          // Add to group
          return updateSpace(space.siteId, space.id, { groupId });
        } else if (!shouldBeInGroup && isInGroup) {
          // Remove from group
          return updateSpace(space.siteId, space.id, { groupId: null });
        }
        return null;
      });

      await Promise.all(updates.filter(Boolean));

      // Invalidate spaces queries for all affected sites
      sites.forEach(site => {
        queryClient.invalidateQueries({ queryKey: ["spaces", site.id] });
      });
      // Also invalidate space-groups since group membership changed
      queryClient.invalidateQueries({ queryKey: ["space-groups"] });

      onSuccess?.();
      onOpenChange(false);
    } catch (err) {
      logger.error("Failed to update space assignments:", err);
      setError(
        err instanceof Error ? err.message : "Failed to update space assignments"
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  // Group spaces by site for better organization
  const spacesBySite = allSpaces.reduce<Record<string, Space[]>>((acc, space) => {
    if (!acc[space.siteId]) {
      acc[space.siteId] = [];
    }
    acc[space.siteId].push(space);
    return acc;
  }, {});

  // Get site name for display
  const getSiteName = (siteId: string) => {
    const site = sites.find((s) => s.id === siteId);
    return site ? site.name : siteId;
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[80vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Manage Spaces in "{groupName}"</DialogTitle>
        </DialogHeader>

        <ScrollArea className="flex-1 pr-4">
          <div className="space-y-4">
            {isLoading ? (
              <div className="text-center py-8 text-sm text-muted-foreground">
                Loading spaces...
              </div>
            ) : allSpaces.length === 0 ? (
              <div className="text-center py-8 border rounded-lg border-dashed">
                <p className="text-sm text-muted-foreground">
                  No spaces available.
                </p>
                <p className="text-xs text-muted-foreground mt-1">
                  Create spaces first before assigning them to groups.
                </p>
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between pb-2 border-b">
                  <div className="flex items-center gap-2">
                    <Checkbox
                      checked={selectedSpaceIds.size === allSpaces.length}
                      onCheckedChange={handleSelectAll}
                      disabled={isSubmitting}
                    />
                    <span className="text-sm font-medium">Select All</span>
                  </div>
                  <Badge variant="outline">
                    {selectedSpaceIds.size} of {allSpaces.length} selected
                  </Badge>
                </div>

                {Object.entries(spacesBySite).map(([siteId, spaces]) => (
                  <div key={siteId} className="space-y-2">
                    <h4 className="text-sm font-medium text-muted-foreground">
                      {getSiteName(siteId)}
                    </h4>
                    <div className="space-y-2 pl-4">
                      {spaces.map((space) => (
                        <div
                          key={space.id}
                          className="flex items-center gap-3 p-2 rounded-md hover:bg-muted/50"
                        >
                          <Checkbox
                            checked={selectedSpaceIds.has(space.id)}
                            onCheckedChange={() => handleToggleSpace(space.id)}
                            disabled={isSubmitting}
                          />
                          <div className="flex-1">
                            <p className="text-sm font-medium">{space.name}</p>
                            {space.code && (
                              <p className="text-xs text-muted-foreground font-mono">
                                {space.code}
                              </p>
                            )}
                          </div>
                          {space.groupId && space.groupId !== groupId && (
                            <Badge variant="outline" className="text-xs">
                              In another group
                            </Badge>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
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

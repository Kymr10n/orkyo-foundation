/**
 * EditSpaceDialog - Form dialog for editing an existing space
 *
 * Features:
 * - Edit name, code, and description
 * - Code is read-only (cannot be changed after creation)
 * - Error handling and validation
 */

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
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import type { Space } from "@foundation/src/types/space";
import { Loader2 } from "lucide-react";
import { useEffect, useState } from "react";
import { useUpdateSpace } from "@foundation/src/hooks/useSpaces";

interface EditSpaceDialogProps {
  space: Space;
  siteId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (space: Space) => void;
}

export function EditSpaceDialog({
  space,
  siteId,
  open,
  onOpenChange,
  onSuccess,
}: EditSpaceDialogProps) {
  const [name, setName] = useState(space.name);
  const [description, setDescription] = useState(space.description || "");
  const [capacity, setCapacity] = useState(space.capacity ?? 1);
  const [error, setError] = useState<string | null>(null);
  
  const updateMutation = useUpdateSpace(siteId);
  const isSubmitting = updateMutation.isPending;

  useEffect(() => {
    if (open) {
      setName(space.name);
      setDescription(space.description || "");
      setCapacity(space.capacity ?? 1);
      setError(null);
    }
  }, [open, space]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      await updateMutation.mutateAsync({
        spaceId: space.id,
        data: {
          name: name.trim(),
          code: space.code, // Keep existing code
          description: description.trim() || undefined,
          isPhysical: space.isPhysical,
          geometry: space.geometry,
          capacity,
        },
      });
      onSuccess(space); // Just close dialog, cache will update
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update space");
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Edit Space</DialogTitle>
          <DialogDescription className="sr-only">Update the name, description, and capacity for this space.</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
            {/* Code (read-only) */}
            <div className="space-y-2">
              <Label htmlFor="code">Code</Label>
              <Input
                id="code"
                value={space.code || ""}
                disabled
                className="bg-muted"
              />
              <p className="text-xs text-muted-foreground">
                Code cannot be changed after creation
              </p>
            </div>

            {/* Name */}
            <div className="space-y-2">
              <Label htmlFor="name">
                Name <span className="text-destructive">*</span>
              </Label>
              <Input
                id="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g., Assembly Area A"
                disabled={isSubmitting}
                autoFocus
              />
            </div>

            {/* Description */}
            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Textarea
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Optional description..."
                disabled={isSubmitting}
                rows={3}
              />
            </div>

            {/* Capacity */}
            <div className="space-y-2">
              <Label htmlFor="capacity">Capacity</Label>
              <Input
                id="capacity"
                type="number"
                min={1}
                value={capacity}
                onChange={(e) => setCapacity(Math.max(1, parseInt(e.target.value) || 1))}
                disabled={isSubmitting}
              />
              <p className="text-xs text-muted-foreground">
                Number of concurrent allocations allowed (e.g., 5 for a hot desk area with 5 desks)
              </p>
            </div>

            {/* Error message */}
            {error && (
              <div className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-md p-3">
                {error}
              </div>
            )}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              )}
              Save Changes
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

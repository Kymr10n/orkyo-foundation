/**
 * EditSpaceDialog - Form dialog for editing an existing space
 *
 * Features:
 * - Edit name, code, and description
 * - Code is read-only (cannot be changed after creation)
 * - Error handling and validation
 */

import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import type { Space } from "@foundation/src/types/space";
import { useEffect, useMemo, useState } from "react";
import { useUpdateSpace } from "@foundation/src/hooks/useSpaces";
import { useDialogDirtyGuard } from "@foundation/src/hooks/useDialogDirtyGuard";

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

  const isDirty = useMemo(
    () =>
      name !== space.name ||
      description !== (space.description || "") ||
      capacity !== (space.capacity ?? 1),
    [name, description, capacity, space],
  );

  const { guardedOnOpenChange, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty,
    onOpenChange,
  });

  const handleSubmit = async () => {
    setError(null);

    if (!name.trim()) {
      setError("Name is required");
      return;
    }

    try {
      await updateMutation.mutateAsync({
        resourceId: space.id,
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
    <>
      <FormDialog
        open={open}
        onOpenChange={guardedOnOpenChange}
        title="Edit Space"
        description="Update the name, description, and capacity for this space."
        srOnlyDescription
        error={error}
        onSubmit={handleSubmit}
        isSubmitting={isSubmitting}
        submitLabel="Save Changes"
      >
        {/* Code (read-only) */}
        <div className="space-y-2">
          <Label htmlFor="code">Code</Label>
          <Input id="code" value={space.code || ""} disabled className="bg-muted" />
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
      </FormDialog>
      {ConfirmDiscardDialog}
    </>
  );
}

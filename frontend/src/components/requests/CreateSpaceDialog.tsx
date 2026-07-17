/**
 * CreateSpaceDialog - Form dialog for creating a new space after drawing
 *
 * Features:
 * - Name and code input with validation
 * - Properties configuration (optional)
 * - Preview of drawn geometry
 * - Error handling
 */

import { useState } from 'react';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import type { SpaceGeometry, CreateSpaceRequest } from '@foundation/src/types/space';
import { useDialogDirtyGuard } from '@foundation/src/hooks/useDialogDirtyGuard';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface CreateSpaceDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  geometry: SpaceGeometry;
  onSubmit: (request: CreateSpaceRequest) => Promise<void>;
  siteId: string;
}

export function CreateSpaceDialog({
  open,
  onOpenChange,
  geometry,
  onSubmit,
  siteId: _siteId,
}: CreateSpaceDialogProps) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isDirty = name !== '' || code !== '' || description !== '';

  const { guardedOnOpenChange, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty,
    onOpenChange,
  });

  const handleSubmit = async () => {
    setError(null);

    if (!name.trim()) {
      setError('Name is required');
      return;
    }

    setIsSubmitting(true);

    try {
      const request: CreateSpaceRequest = {
        name: name.trim(),
        code: code.trim() || undefined,
        description: description.trim() || undefined,
        isPhysical: true,
        geometry,
      };

      await onSubmit(request);

      // Reset form
      setName('');
      setCode('');
      setDescription('');
      onOpenChange(false);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  const geometryInfo = `${geometry.type} with ${geometry.coordinates.length} points`;

  return (
    <>
      <FormDialog
        open={open}
        onOpenChange={guardedOnOpenChange}
        title="Create New Space"
        description="Define the space details for the area you've drawn on the floorplan."
        error={error}
        onSubmit={handleSubmit}
        isSubmitting={isSubmitting}
        submitLabel="Create Space"
      >
        {/* Geometry info */}
        <div className="rounded-lg bg-muted p-3 text-sm">
          <p className="font-medium">Geometry: {geometryInfo}</p>
          <p className="text-xs text-muted-foreground mt-1">
            Coordinates: {JSON.stringify(geometry.coordinates.slice(0, 2))}
            {geometry.coordinates.length > 2 && '...'}
          </p>
        </div>

        {/* Name */}
        <div className="space-y-2">
          <Label htmlFor="space-name" className="required">
            Name <span className="text-destructive">*</span>
          </Label>
          <Input
            id="space-name"
            placeholder="e.g., Assembly Zone A"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={isSubmitting}
            required
            autoFocus
          />
        </div>

        {/* Code */}
        <div className="space-y-2">
          <Label htmlFor="space-code">Code</Label>
          <Input
            id="space-code"
            placeholder="e.g., A-01"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            disabled={isSubmitting}
          />
          <p className="text-xs text-muted-foreground">
            Optional short identifier for this space
          </p>
        </div>

        {/* Description */}
        <div className="space-y-2">
          <Label htmlFor="space-description">Description</Label>
          <Textarea
            id="space-description"
            placeholder="Optional description..."
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={isSubmitting}
            rows={3}
          />
        </div>
      </FormDialog>
      {ConfirmDiscardDialog}
    </>
  );
}

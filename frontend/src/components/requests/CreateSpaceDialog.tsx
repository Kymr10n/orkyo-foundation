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
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogFooter } from '@foundation/src/components/ui/dialog';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Loader2 } from 'lucide-react';
import type { SpaceGeometry, CreateSpaceRequest } from '@foundation/src/types/space';

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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
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
      setError(err instanceof Error ? err.message : 'Failed to create space');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCancel = () => {
    setName('');
    setCode('');
    setDescription('');
    setError(null);
    onOpenChange(false);
  };

  const geometryInfo = `${geometry.type} with ${geometry.coordinates.length} points`;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Create New Space</DialogTitle>
          <DialogDescription>
            Define the space details for the area you've drawn on the floorplan.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
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

            {/* Error message */}
            {error && (
              <div className="rounded-lg bg-destructive/10 border border-destructive/20 p-3 text-sm text-destructive">
                {error}
              </div>
            )}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={handleCancel}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Create Space
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

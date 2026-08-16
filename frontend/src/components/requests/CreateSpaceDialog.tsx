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
import type { CreateResourceRequest } from '@foundation/src/lib/api/resources-api';
import type { ResourceGeometry } from '@foundation/src/types/geometry';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface CreateSpaceDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  geometry: ResourceGeometry;
  onSubmit: (request: CreateResourceRequest) => Promise<void>;
  siteId: string;
}

export function CreateSpaceDialog({
  open,
  onOpenChange,
  geometry,
  onSubmit,
  siteId,
}: CreateSpaceDialogProps) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Anything that occupies area can be drawn, so the type is only a question when the tenant has
  // defined more than one. With a single placeable type there is nothing to ask.
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);
  const defaultTypeKey =
    placeableTypes.find((t) => t.key === RESOURCE_TYPE_KEY.SPACE)?.key
    ?? placeableTypes[0]?.key
    ?? RESOURCE_TYPE_KEY.SPACE;
  const [typeKey, setTypeKey] = useState<string | null>(null);
  const selectedTypeKey = typeKey ?? defaultTypeKey;

  const isDirty = name !== '' || code !== '' || description !== '' || typeKey !== null;

  const handleSubmit = async () => {
    setError(null);

    if (!name.trim()) {
      setError('Name is required');
      return;
    }

    setIsSubmitting(true);

    try {
      // The defaults the site-scoped space route used to supply server-side. The backend still
      // enforces them, so a client that got this wrong fails loudly rather than creating a
      // placeable resource that could be assigned away from its floorplan.
      const request: CreateResourceRequest = {
        resourceTypeKey: selectedTypeKey,
        name: name.trim(),
        code: code.trim() || undefined,
        description: description.trim() || undefined,
        allocationMode: 'Exclusive',
        homeSiteId: siteId,
        crossSiteAllowed: false,
        isPhysical: true,
        geometry,
      };

      await onSubmit(request);

      // Reset form
      setName('');
      setCode('');
      setDescription('');
      setTypeKey(null);
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
        onOpenChange={onOpenChange}
        dirty={isDirty}
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

        {/* Type — only when the tenant has more than one thing that can occupy area. */}
        {placeableTypes.length > 1 && (
          <div className="space-y-2">
            <Label htmlFor="placeable-type">Type</Label>
            <Select value={selectedTypeKey} onValueChange={setTypeKey} disabled={isSubmitting}>
              <SelectTrigger id="placeable-type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {placeableTypes.map((t) => (
                  <SelectItem key={t.key} value={t.key}>{t.displayName}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

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
    </>
  );
}

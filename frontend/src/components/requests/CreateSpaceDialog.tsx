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
import { useResourceCustomFieldForm } from '@foundation/src/hooks/useResourceCustomFieldForm';
import { CustomFieldInput } from '@foundation/src/components/resources/CustomFieldInput';
import type { CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface CreateSpaceDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  geometry: ResourceGeometry;
  /** What the toolbar was armed with when the shape was drawn. */
  resourceTypeKey: string;
  /** Its id, for the type's custom-field definitions. Required — the dialog only opens after a
   *  shape is drawn, and drawing requires an armed type. */
  resourceTypeId: string;
  /** That type's display name, for the read-only summary. Passed rather than re-resolved so this
   *  dialog stays a plain form with no data dependencies of its own. */
  resourceTypeLabel: string;
  onSubmit: (request: CreateResourceRequest) => Promise<void>;
  siteId: string;
}

export function CreateSpaceDialog({
  open,
  onOpenChange,
  geometry,
  resourceTypeKey,
  resourceTypeId,
  resourceTypeLabel,
  onSubmit,
  siteId,
}: CreateSpaceDialogProps) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // The type's custom fields, required ones included. This form used to send none, which is why
  // the API had to refuse required fields on the built-in placeable type outright — the only
  // create path would have been unable to satisfy them. Asking here is what lifts that.
  const [customFieldValues, setCustomFieldValues] = useState<Record<string, CustomFieldValue>>({});
  const customFields = useResourceCustomFieldForm(resourceTypeId, open);
  const setCustomField = (key: string, value: CustomFieldValue) =>
    setCustomFieldValues((current) => customFields.withValue(current, key, value));

  const isDirty = name !== '' || code !== '' || description !== '';

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
        resourceTypeKey,
        name: name.trim(),
        code: code.trim() || undefined,
        description: description.trim() || undefined,
        allocationMode: 'Exclusive',
        homeSiteId: siteId,
        crossSiteAllowed: false,
        isPhysical: true,
        geometry,
        customFields: customFields.forSave(customFieldValues),
      };

      await onSubmit(request);

      // Reset form
      setName('');
      setCode('');
      setDescription('');
      setCustomFieldValues({});
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
        submitDisabled={!customFields.isSatisfied(customFieldValues)}
        submitLabel="Create Space"
      >
        {/* Geometry info */}
        <div className="rounded-lg bg-muted p-3 text-sm">
          <p className="font-medium">Type: {resourceTypeLabel}</p>
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

        {/* Custom fields defined on the chosen type — the same block the edit dialog renders,
            so a field behaves identically whether the station is being drawn or revisited. */}
        {customFields.fields.length > 0 && (
          <div className="space-y-4 border-t pt-4">
            {customFields.fields.map((field) => (
              <CustomFieldInput
                key={field.id}
                field={field}
                value={customFields.valueOf(field, customFieldValues)}
                onChange={(value) => setCustomField(field.key, value)}
              />
            ))}
          </div>
        )}
      </FormDialog>
    </>
  );
}

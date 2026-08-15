/**
 * EditSpaceDialog - Form dialog for editing an existing space
 *
 * Features:
 * - Edit name, code, and description
 * - Code is read-only (cannot be changed after creation)
 * - Error handling and validation
 */

import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { useResourceCustomFieldForm } from '@foundation/src/hooks/useResourceCustomFieldForm';
import { CustomFieldInput } from '@foundation/src/components/resources/CustomFieldInput';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import type { CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Textarea } from "@foundation/src/components/ui/textarea";
import type { Space } from "@foundation/src/types/space";
import { useMemo, useState } from "react";
import { useUpdateSpace } from "@foundation/src/hooks/useSpaces";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

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
  const [customFieldValues, setCustomFieldValues] = useState(space.customFields ?? {});
  // Snapshot of the values as last seeded. Dirtiness compares identity against it, which works
  // because every edit builds a new object — comparing against `space.customFields ?? {}`
  // instead would allocate a fresh {} each render and report the form dirty forever.
  const [customFieldsBaseline, setCustomFieldsBaseline] = useState(customFieldValues);
  const [error, setError] = useState<string | null>(null);

  // A space is a resource, so a tenant can put custom fields on it like any other type — lists
  // included, whose rows hang off the space itself. The dialog knows only the key.
  const { data: resourceTypes = [] } = useResourceTypes();
  const spaceTypeId = resourceTypes.find((t) => t.key === RESOURCE_TYPE_KEY.SPACE)?.id;
  const customFields = useResourceCustomFieldForm(spaceTypeId, open);
  const setCustomField = (key: string, value: CustomFieldValue) =>
    setCustomFieldValues((current) => customFields.withValue(current, key, value));

  const updateMutation = useUpdateSpace(siteId);
  const isSubmitting = updateMutation.isPending;

  // Reseed on open / space swap — a render-phase update, not an effect (see useEntityFormDialog.ts).
  const [synced, setSynced] = useState<{ open: boolean; space: Space } | null>(null);
  if (synced?.open !== open || synced.space !== space) {
    setSynced({ open, space });
    if (open) {
      setName(space.name);
      setDescription(space.description || "");
      setCapacity(space.capacity ?? 1);
      const seededCustomFields = space.customFields ?? {};
      setCustomFieldValues(seededCustomFields);
      setCustomFieldsBaseline(seededCustomFields);
      setError(null);
    }
  }

  const isDirty = useMemo(
    () =>
      name !== space.name ||
      description !== (space.description || "") ||
      capacity !== (space.capacity ?? 1) ||
      customFieldValues !== customFieldsBaseline,
    [name, description, capacity, customFieldValues, customFieldsBaseline, space],
  );

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
          customFields: customFields.forSave(customFieldValues),
        },
      });
      onSuccess(space); // Just close dialog, cache will update
      onOpenChange(false);
    } catch (err) {
      setError(errorMessage(err));
    }
  };

  return (
    <>
      <FormDialog
        open={open}
        onOpenChange={onOpenChange}
        dirty={isDirty}
        title="Edit Space"
        description="Update the name, description, and capacity for this space."
        srOnlyDescription
        error={error}
        onSubmit={handleSubmit}
        isSubmitting={isSubmitting}
        submitLabel="Save Changes"
        submitDisabled={!customFields.isSatisfied(customFieldValues)}
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

        {/* Custom fields defined on the space type — the same block the resource and person
            forms render, so a field behaves identically wherever its type is edited. */}
        {customFields.fields.length > 0 && (
          <div className="space-y-4 border-t pt-4">
            {customFields.fields.map((field) => (
              <CustomFieldInput
                key={field.id}
                field={field}
                value={customFields.valueOf(field, customFieldValues)}
                onChange={(value) => setCustomField(field.key, value)}
                resourceId={space.id}
              />
            ))}
          </div>
        )}
      </FormDialog>
    </>
  );
}

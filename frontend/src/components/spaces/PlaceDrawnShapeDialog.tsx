import { useMemo, useState } from 'react';
import { Button } from '@foundation/src/components/ui/button';
import { Combobox } from '@foundation/src/components/ui/combobox';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Label } from '@foundation/src/components/ui/label';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

interface PlaceDrawnShapeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Resources of the drawn type that exist but carry no shape yet. Never empty — the caller
   *  goes straight to the create form when there is nothing to assign. */
  candidates: ResourceInfo[];
  /** The armed type's display name, for the wording. */
  resourceTypeLabel: string;
  /** Gives the drawn shape to an existing resource. */
  onAssign: (resourceId: string) => Promise<void>;
  /** Drops through to the create form, carrying the same shape. */
  onCreateNew: () => void;
}

/**
 * What a finished shape becomes: an existing resource that had no place yet, or a new one.
 *
 * The second half is the older behaviour and stays one click away. The first exists because a
 * resource can be registered long before anyone draws it — imported from a spreadsheet, or added
 * from its type's list page — and without this the only way to put it on the plan was to draw a
 * second resource and delete the first, losing whatever the original carried.
 *
 * Only offered when there is something to offer. With no unplaced resource of the drawn type the
 * caller skips this dialog entirely, so nobody is asked to choose between one real option and a
 * dead end.
 */
export function PlaceDrawnShapeDialog({
  open,
  onOpenChange,
  candidates,
  resourceTypeLabel,
  onAssign,
  onCreateNew,
}: PlaceDrawnShapeDialogProps) {
  const [selectedId, setSelectedId] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Code first, matching how the floorplan and the scheduler name a resource; the name alone
  // repeats across sites often enough that a code is what tells two apart.
  const options = useMemo(
    () =>
      candidates.map((resource) => ({
        id: resource.id,
        label: resource.code ? `${resource.code} — ${resource.name}` : resource.name,
      })),
    [candidates],
  );

  const handleSubmit = async () => {
    setError(null);
    setIsSubmitting(true);
    try {
      await onAssign(selectedId);
    } catch {
      // The mutation's own toast already reported it; this keeps the dialog open so the choice
      // is not lost, and says so where the reader is looking.
      setError(`Could not place this ${resourceTypeLabel.toLowerCase()}. Nothing was changed.`);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Place ${resourceTypeLabel}`}
      description={`${candidates.length === 1 ? 'One' : candidates.length} ${resourceTypeLabel.toLowerCase()}${candidates.length === 1 ? '' : 's'} at this site have no place on the plan yet. Give the shape to one of them, or create a new one.`}
      error={error}
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Place here"
      submitDisabled={selectedId === ''}
    >
      <div className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="place-existing-resource">Not yet on the plan</Label>
          <Combobox
            id="place-existing-resource"
            value={selectedId}
            onChange={setSelectedId}
            options={options}
            placeholder={`Choose a ${resourceTypeLabel.toLowerCase()}…`}
            emptyText="No matches"
          />
        </div>

        <div className="border-t pt-3">
          <Button type="button" variant="outline" className="w-full" onClick={onCreateNew}>
            Create a new {resourceTypeLabel.toLowerCase()} instead
          </Button>
        </div>
      </div>
    </FormDialog>
  );
}

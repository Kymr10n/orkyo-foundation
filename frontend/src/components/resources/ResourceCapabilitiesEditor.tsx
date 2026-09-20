import { useMemo, useState } from 'react';
import { Sparkles } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import {
  useCriteriaForResourceType,
  useInvalidateCriteriaForResourceType,
} from '@foundation/src/hooks/useCriteria';
import {
  useResourceCapabilities,
  useSaveResourceCapabilities,
} from '@foundation/src/hooks/useResources';
import type { Criterion, CriterionValue } from '@foundation/src/types/criterion';
import { logger } from '@foundation/src/lib/core/logger';
import { CriterionEditDialog } from '../settings/CriterionEditDialog';
import { CriterionAssignmentEditor } from '../capabilities/CriterionAssignmentEditor';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

export interface ResourceCapabilitiesEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  resourceId: string;
  resourceName: string;
  /** Only criteria applicable to this type are offered, and quick-create defaults to it. */
  resourceTypeKey: string;
  /**
   * What this type calls its criterion values. People call them "Skills"; most types just
   * call them "Capabilities". Purely cosmetic — the storage is identical either way.
   */
  valueLabel?: { plural: string; singular: string };
  /** e.g. "person", "car" — used in the sentences describing the dialog. */
  entityLabel?: string;
}

/**
 * Criterion-value assignment for ANY resource. A thin wrapper over the shared
 * CriterionAssignmentEditor: loads the criteria applicable to this resource type plus the
 * resource's existing values, upserts on save (the backend POST upserts), and offers
 * quick-create of a new criterion already tagged for this type.
 *
 * Distinct from a resource's custom fields, which live on the resource form: those are
 * descriptive only. A criterion is the matchable kind of attribute — a request can require
 * it and the solver reasons over it, which is why it is assigned here rather than typed in
 * alongside the name.
 */
export function ResourceCapabilitiesEditor({
  open,
  onOpenChange,
  resourceId,
  resourceName,
  resourceTypeKey,
  valueLabel = { plural: 'Capabilities', singular: 'Capability' },
  entityLabel = 'resource',
}: ResourceCapabilitiesEditorProps) {
  const [selectedCriterionId, setSelectedCriterionId] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const invalidateCriteria = useInvalidateCriteriaForResourceType(resourceTypeKey);

  const lowerPlural = valueLabel.plural.toLowerCase();

  const {
    data: caps,
    isLoading: capsLoading,
    error: capsError,
  } = useResourceCapabilities(resourceId, open);

  const { data: availableCriteria = [], isLoading: criteriaLoading } =
    useCriteriaForResourceType(resourceTypeKey, open);

  const initialAssignments = useMemo(() => {
    const map = new Map<string, CriterionValue | null>();
    for (const cap of caps ?? []) map.set(cap.criterionId, cap.value);
    return map;
  }, [caps]);

  const loadError = capsError
    ? capsError instanceof Error
      ? capsError.message
      : `Failed to load ${lowerPlural}`
    : null;

  const saveMutation = useSaveResourceCapabilities(resourceId, valueLabel);

  const handleSave = (desired: Map<string, CriterionValue | null>) =>
    saveMutation.mutate(desired, {
      onSuccess: () => {
        setSaveError(null);
        onOpenChange(false);
      },
      onError: (err) => {
        logger.error('Failed to save resource capabilities:', err);
        setSaveError(errorMessage(err));
      },
    });

  const handleCriterionCreated = async (criterion: Criterion) => {
    // Refresh the applicable criteria so the new one appears; preselect it.
    await invalidateCriteria();
    setSelectedCriterionId(criterion.id);
  };

  return (
    <>
      <CriterionAssignmentEditor
        open={open}
        onOpenChange={onOpenChange}
        criteria={availableCriteria}
        isLoading={capsLoading || criteriaLoading}
        loadError={loadError}
        saveError={saveError}
        isSaving={saveMutation.isPending}
        initialAssignments={initialAssignments}
        onSave={handleSave}
        selectedCriterionId={selectedCriterionId}
        onSelectedCriterionIdChange={setSelectedCriterionId}
        labels={{
          title: `${valueLabel.plural} for ${resourceName}`,
          srDescription: `Manage ${lowerPlural} for this ${entityLabel}.`,
          intro: `Assign criterion values describing this ${entityLabel}'s ${lowerPlural}.`,
          sectionLabel: valueLabel.plural,
          selectPlaceholder: `Select a ${valueLabel.singular.toLowerCase()} to add`,
          emptyText: `No ${lowerPlural} assigned yet.`,
          selectableEmptyText: `All criteria applicable to this ${entityLabel} are already assigned, or none exist yet.`,
        }}
        addSlot={
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setCreateOpen(true)}
            disabled={saveMutation.isPending}
          >
            <Sparkles className="h-4 w-4 mr-2" />
            Add {valueLabel.singular} Criterion
          </Button>
        }
      />

      <CriterionEditDialog
        criterion={null}
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSaved={handleCriterionCreated}
        defaultResourceType={resourceTypeKey}
      />
    </>
  );
}

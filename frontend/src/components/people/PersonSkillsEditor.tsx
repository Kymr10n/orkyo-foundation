import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Sparkles } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { getCriteria } from '@foundation/src/lib/api/criteria-api';
import {
  deleteResourceCapability,
  getResourceCapabilities,
  upsertResourceCapability,
} from '@foundation/src/lib/api/resource-capabilities-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { Criterion, CriterionValue } from '@foundation/src/types/criterion';
import { logger } from '@foundation/src/lib/core/logger';
import { CriterionEditDialog } from '../settings/CriterionEditDialog';
import { CriterionAssignmentEditor } from '../capabilities/CriterionAssignmentEditor';
import { diffCapabilityAssignments } from '../capabilities/capability-diff';
import { errorMessage } from '@foundation/src/hooks/mutation-utils';

interface PersonSkillsEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  resourceId: string;
  personName: string;
}

const PERSON_CRITERIA_KEY = qk.criteria.byResourceType('person');

/**
 * Per-person skill (criterion) assignment editor. A thin wrapper over the shared
 * CriterionAssignmentEditor: loads person-applicable criteria + existing skills,
 * upserts on save (the backend POST upserts), and offers quick-create.
 */
export function PersonSkillsEditor({
  open,
  onOpenChange,
  resourceId,
  personName,
}: PersonSkillsEditorProps) {
  const [selectedCriterionId, setSelectedCriterionId] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const queryClient = useQueryClient();

  const { data: caps, isLoading: capsLoading, error: capsError } = useQuery({
    queryKey: qk.resources.capabilities(resourceId),
    queryFn: () => getResourceCapabilities(resourceId),
    enabled: open,
  });

  const { data: availableCriteria = [], isLoading: criteriaLoading } = useQuery({
    queryKey: PERSON_CRITERIA_KEY,
    queryFn: () => getCriteria({ resourceType: 'person' }),
    enabled: open,
  });

  const initialAssignments = useMemo(() => {
    const map = new Map<string, CriterionValue | null>();
    for (const cap of caps ?? []) map.set(cap.criterionId, cap.value);
    return map;
  }, [caps]);

  const loadError = capsError
    ? capsError instanceof Error ? capsError.message : 'Failed to load skills'
    : null;

  const saveMutation = useMutation({
    mutationFn: async (desired: Map<string, CriterionValue | null>) => {
      const existing = await getResourceCapabilities(resourceId);
      const { toPersist, toDeleteIds } = diffCapabilityAssignments(existing, desired, 'upsert');
      await Promise.all([
        ...toPersist.map((cap) => upsertResourceCapability(resourceId, cap)),
        ...toDeleteIds.map((id) => deleteResourceCapability(resourceId, id)),
      ]);
    },
    meta: {
      successMessage: 'Skills saved',
      errorMessage: 'Failed to save skills',
      invalidates: [qk.resources.capabilities(resourceId)],
    },
    onSuccess: () => {
      setSaveError(null);
      onOpenChange(false);
    },
    onError: (err) => {
      logger.error('Failed to save person skills:', err);
      setSaveError(errorMessage(err));
    },
  });

  const handleCriterionCreated = async (criterion: Criterion) => {
    // Refresh the person-applicable criteria so the new one appears; preselect it.
    await queryClient.invalidateQueries({ queryKey: PERSON_CRITERIA_KEY });
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
        onSave={(desired) => saveMutation.mutate(desired)}
        selectedCriterionId={selectedCriterionId}
        onSelectedCriterionIdChange={setSelectedCriterionId}
        labels={{
          title: `Skills for ${personName}`,
          srDescription: 'Manage skill assignments for this person.',
          intro: "Assign criterion values describing this person's skills and qualifications.",
          sectionLabel: 'Skills',
          selectPlaceholder: 'Select a skill to add',
          emptyText: 'No skills assigned yet.',
          selectableEmptyText: 'All person-applicable criteria are already assigned, or none exist yet.',
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
            Add Skill Criterion
          </Button>
        }
      />

      <CriterionEditDialog
        criterion={null}
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSaved={handleCriterionCreated}
        defaultResourceType="person"
      />
    </>
  );
}

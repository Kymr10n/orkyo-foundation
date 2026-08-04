import { useState, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { Plus, Pencil, Trash2, Sliders, CalendarOff } from 'lucide-react';
import { PersonEditDialog } from './PersonEditDialog';
import { PersonSkillsEditor } from './PersonSkillsEditor';
import { ResourceAbsenceList } from '../resources/ResourceAbsenceList';
import { getResources, deleteResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { getPersonProfiles, type PersonProfileInfo } from '@foundation/src/lib/api/person-profiles-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useEditQueryParam } from '@foundation/src/hooks/useEditQueryParam';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';

/**
 * A person resource enriched with its profile fields, so table columns can read
 * them via accessors (which is what makes them sortable/filterable) instead of
 * closing over the profile map.
 */
type PersonRow = ResourceInfo & {
  email: string;
  jobTitle: string;
  department: string;
};

export function PersonList() {
  const canEdit = useCanEdit();
  const [editingPerson, setEditingPerson] = useState<ResourceInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [skillsPerson, setSkillsPerson] = useState<ResourceInfo | null>(null);
  const [absencePerson, setAbsencePerson] = useState<ResourceInfo | null>(null);
  const [deletingPerson, setDeletingPerson] = useState<ResourceInfo | null>(null);

  const { data: people, isLoading, error, refetch } = useQuery({
    queryKey: qk.resources.byType(RESOURCE_TYPE_KEY.PERSON),
    queryFn: () => getResources({ resourceTypeKey: 'person' }),
  });

  // Open the edit dialog when navigated here with ?edit=<id> from global search.
  useEditQueryParam(people?.data, (person: ResourceInfo) => {
    setEditingPerson(person);
    setIsDialogOpen(true);
  });

  // Fetch every person's profile in one batched request (replaces the old
  // per-row fan-out). Resources without a profile row are simply absent from the
  // response; callers index by resourceId, so a missing entry reads as null.
  const personRows = useMemo<ResourceInfo[]>(() => people?.data ?? [], [people]);
  const personIds = useMemo(() => personRows.map((p) => p.id), [personRows]);
  const { data: profiles = [] } = useQuery({
    queryKey: qk.personProfiles.byIds(personIds),
    queryFn: () => getPersonProfiles(personIds),
    enabled: personIds.length > 0,
    staleTime: 60_000,
  });
  const profileByPersonId = useMemo(() => {
    const map: Record<string, PersonProfileInfo | null> = {};
    for (const p of profiles) map[p.resourceId] = p;
    return map;
  }, [profiles]);

  // Merge profile fields onto the rows so columns read them via accessors.
  const rows = useMemo<PersonRow[]>(
    () =>
      personRows.map((p) => {
        const profile = profileByPersonId[p.id];
        return {
          ...p,
          email: profile?.email ?? '',
          jobTitle: profile?.jobTitleName ?? '',
          department: profile?.departmentPath ?? '',
        };
      }),
    [personRows, profileByPersonId],
  );

  const queryClient = useQueryClient();

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteResource(id),
    meta: {
      successMessage: 'Person deactivated',
      errorMessage: 'Failed to deactivate person',
      invalidates: [qk.resources.byType(RESOURCE_TYPE_KEY.PERSON)],
    },
    onSuccess: () => setDeletingPerson(null),
  });

  const handleEdit = (person: ResourceInfo | null) => {
    setEditingPerson(person);
    setIsDialogOpen(true);
  };

  const handleConfirmDelete = () => {
    if (deletingPerson) deleteMutation.mutate(deletingPerson.id);
  };

  const handleDialogClose = () => {
    setIsDialogOpen(false);
    setEditingPerson(null);
  };

  const handlePersonSaved = () => {
    queryClient.invalidateQueries({ queryKey: qk.resources.byType(RESOURCE_TYPE_KEY.PERSON) });
    handleDialogClose();
  };

  // Shared row actions — used by the desktop table cell and the phone card. The
  // wrapper stops propagation so taps never trigger the row/card edit-onClick.
  const renderActions = (person: ResourceInfo) => (
    <RowActions
      triggerLabel={`Actions for ${person.name}`}
      actions={[
        { label: 'Edit Person', icon: Pencil, onSelect: () => handleEdit(person) },
        { label: 'Manage Skills', icon: Sliders, onSelect: () => setSkillsPerson(person), disabled: !canEdit },
        { label: 'Manage Absences', icon: CalendarOff, onSelect: () => setAbsencePerson(person), disabled: !canEdit },
        { label: 'Deactivate Person', icon: Trash2, onSelect: () => setDeletingPerson(person), disabled: !canEdit, destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<PersonRow>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
    },
    {
      accessorKey: 'email',
      header: 'Email',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-muted-foreground">
          {row.original.email || '-'}
        </span>
      ),
    },
    {
      accessorKey: 'jobTitle',
      header: 'Job Title',
      meta: { filter: { type: 'enum' } },
      cell: ({ row }) => row.original.jobTitle || '-',
    },
    {
      accessorKey: 'department',
      header: 'Department',
      meta: { filter: { type: 'enum' } },
      cell: ({ row }) => row.original.department || '-',
    },
    {
      id: 'actions',
      header: () => null,
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Phone presentation: name + contact stacked, actions trailing.
  const renderCard = (person: PersonRow) => {
    const subtitle = [person.jobTitle, person.department].filter(Boolean).join(' · ');
    return (
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 space-y-0.5">
          <p className="font-medium truncate">{person.name}</p>
          <p className="text-sm text-muted-foreground truncate">{person.email || '-'}</p>
          <p className="text-xs text-muted-foreground truncate">{subtitle || '-'}</p>
        </div>
        {renderActions(person)}
      </div>
    );
  };

  const queryErrorMsg = error instanceof Error ? error.message : error ? String(error) : null;

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('people', columns);

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => handleEdit(null)} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add Person
        </Button>
      </div>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={rows}
        isLoading={isLoading}
        error={queryErrorMsg}
        onRetry={() => refetch()}
        emptyMessage="No people found. Add your first person to get started."
        emptyAction={
          <Button onClick={() => handleEdit(null)} disabled={!canEdit}>
            <Plus className="h-4 w-4 mr-2" />
            Add Person
          </Button>
        }
        pageSize={25}
        onRowClick={handleEdit}
        renderCard={renderCard}
      />

      <PersonEditDialog
        person={editingPerson}
        isOpen={isDialogOpen}
        onClose={handleDialogClose}
        onSaved={handlePersonSaved}
      />

      {skillsPerson && (
        <PersonSkillsEditor
          open={!!skillsPerson}
          onOpenChange={(open) => !open && setSkillsPerson(null)}
          resourceId={skillsPerson.id}
          resourceName={skillsPerson.name}
        />
      )}

      {absencePerson && (
        <ResourceAbsenceList
          open={!!absencePerson}
          onOpenChange={(open) => !open && setAbsencePerson(null)}
          resourceId={absencePerson.id}
          resourceName={absencePerson.name}
        />
      )}

      <ConfirmDialog
        open={!!deletingPerson}
        onOpenChange={(open) => !open && setDeletingPerson(null)}
        title="Deactivate person"
        description={
          deletingPerson
            ? `Are you sure you want to deactivate "${deletingPerson.name}"?`
            : ''
        }
        confirmLabel="Deactivate"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}

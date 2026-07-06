import { useState, useEffect, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { Plus, Pencil, Trash2, Sliders, CalendarOff } from 'lucide-react';
import { PersonEditDialog } from './PersonEditDialog';
import { PersonSkillsEditor } from './PersonSkillsEditor';
import { PersonAbsenceList } from './PersonAbsenceList';
import { getResources, deleteResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { getPersonProfiles, type PersonProfileInfo } from '@foundation/src/lib/api/person-profiles-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';

export function PersonList() {
  const canEdit = useCanEdit();
  const [editingPerson, setEditingPerson] = useState<ResourceInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [skillsPerson, setSkillsPerson] = useState<ResourceInfo | null>(null);
  const [absencePerson, setAbsencePerson] = useState<ResourceInfo | null>(null);
  const [deletingPerson, setDeletingPerson] = useState<ResourceInfo | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();

  const { data: people, isLoading, error } = useQuery({
    queryKey: qk.resources.byType(RESOURCE_TYPE_KEY.PERSON),
    queryFn: () => getResources({ resourceTypeKey: 'person' }),
  });

  // Open edit dialog when navigated here with ?edit=<id> (e.g. from search)
  useEffect(() => {
    const editId = searchParams.get('edit');
    if (!editId || !people?.data) return;
    const target = people.data.find((p: ResourceInfo) => p.id === editId);
    if (target) {
      setEditingPerson(target);
      setIsDialogOpen(true);
      setSearchParams({}, { replace: true });
    }
  }, [searchParams, people, setSearchParams]);

  // Fetch every person's profile in one batched request (replaces the old
  // per-row fan-out). Resources without a profile row are simply absent from the
  // response; callers index by resourceId, so a missing entry reads as null.
  const personRows = useMemo<ResourceInfo[]>(() => people?.data ?? [], [people]);
  const personIds = useMemo(() => personRows.map((p) => p.id), [personRows]);
  const { data: profiles = [] } = useQuery({
    queryKey: ['person-profiles', personIds],
    queryFn: () => getPersonProfiles(personIds),
    enabled: personIds.length > 0,
    staleTime: 60_000,
  });
  const profileByPersonId = useMemo(() => {
    const map: Record<string, PersonProfileInfo | null> = {};
    for (const p of profiles) map[p.resourceId] = p;
    return map;
  }, [profiles]);

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

  // profileByPersonId is rebuilt when the batched profiles query resolves, so
  // columns must also rebuild each render to close over the latest snapshot.
  const columns: ColumnDef<ResourceInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
    },
    {
      id: 'email',
      header: 'Email',
      cell: ({ row }) => (
        <span className="text-muted-foreground">
          {profileByPersonId[row.original.id]?.email ?? '-'}
        </span>
      ),
    },
    {
      id: 'jobTitle',
      header: 'Job Title',
      cell: ({ row }) => profileByPersonId[row.original.id]?.jobTitleName ?? '-',
    },
    {
      id: 'department',
      header: 'Department',
      cell: ({ row }) => profileByPersonId[row.original.id]?.departmentPath ?? '-',
    },
    {
      id: 'actions',
      header: () => null,
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Phone presentation: name + contact stacked, actions trailing.
  const renderCard = (person: ResourceInfo) => {
    const profile = profileByPersonId[person.id];
    const subtitle = [profile?.jobTitleName, profile?.departmentPath].filter(Boolean).join(' · ');
    return (
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 space-y-0.5">
          <p className="font-medium truncate">{person.name}</p>
          <p className="text-sm text-muted-foreground truncate">{profile?.email ?? '-'}</p>
          <p className="text-xs text-muted-foreground truncate">{subtitle || '-'}</p>
        </div>
        {renderActions(person)}
      </div>
    );
  };

  const queryErrorMsg = error instanceof Error ? error.message : error ? String(error) : null;

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => handleEdit(null)} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add Person
        </Button>
      </div>

      <OrkyoDataTable
        columns={columns}
        data={personRows}
        isLoading={isLoading}
        error={queryErrorMsg}
        emptyMessage="No people found. Add your first person to get started."
        filterColumn="name"
        filterPlaceholder="Search people..."
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
          personName={skillsPerson.name}
        />
      )}

      {absencePerson && (
        <PersonAbsenceList
          open={!!absencePerson}
          onOpenChange={(open) => !open && setAbsencePerson(null)}
          personId={absencePerson.id}
          personName={absencePerson.name}
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

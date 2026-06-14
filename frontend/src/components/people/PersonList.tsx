import { useState, useEffect } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { Plus, Pencil, Trash2, Sliders, CalendarOff } from 'lucide-react';
import { PersonEditDialog } from './PersonEditDialog';
import { PersonSkillsEditor } from './PersonSkillsEditor';
import { PersonAbsenceList } from './PersonAbsenceList';
import { getResources, deleteResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';

export function PersonList() {
  const canEdit = useCanEdit();
  const [editingPerson, setEditingPerson] = useState<ResourceInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [skillsPerson, setSkillsPerson] = useState<ResourceInfo | null>(null);
  const [absencePerson, setAbsencePerson] = useState<ResourceInfo | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();

  const { data: people, isLoading, error } = useQuery({
    queryKey: ['resources', 'person'],
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

  // Fetch each person's profile in parallel. The backend has no batch endpoint
  // for profiles yet; this keeps the grid honest (instead of showing "-" for
  // every row) without scope-creeping into a new endpoint. If this grid grows
  // beyond ~100 people, add GET /api/person-profiles?ids=... as a follow-up.
  const personRows = people?.data ?? [];
  const profileQueries = useQueries({
    queries: personRows.map((p: ResourceInfo) => ({
      queryKey: ['person-profile', p.id] as const,
      queryFn: () => getPersonProfile(p.id).catch(() => null),
      staleTime: 60_000,
    })),
  });
  const profileByPersonId: Record<string, Awaited<ReturnType<typeof getPersonProfile>> | null> = {};
  personRows.forEach((p, idx) => {
    profileByPersonId[p.id] = (profileQueries[idx]?.data ?? null) as
      | Awaited<ReturnType<typeof getPersonProfile>>
      | null;
  });

  const queryClient = useQueryClient();

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteResource(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['resources', 'person'] });
      toast.success('Person deactivated');
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Failed to deactivate person';
      toast.error('Failed to deactivate person', { description: message });
    },
  });

  const handleEdit = (person: ResourceInfo | null) => {
    setEditingPerson(person);
    setIsDialogOpen(true);
  };

  const handleDelete = (id: string) => {
    if (window.confirm('Are you sure you want to deactivate this person?')) {
      deleteMutation.mutate(id);
    }
  };

  const handleDialogClose = () => {
    setIsDialogOpen(false);
    setEditingPerson(null);
  };

  const handlePersonSaved = () => {
    queryClient.invalidateQueries({ queryKey: ['resources', 'person'] });
    handleDialogClose();
  };

  // profileByPersonId is rebuilt by useQueries on every resolve, so columns must
  // also rebuild each render to close over the latest snapshot.
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
      size: 160,
      cell: ({ row }) => {
        const person = row.original;
        return (
          <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
            <Button variant="ghost" size="sm" onClick={() => handleEdit(person)} aria-label={`Edit ${person.name}`} title="Edit Person">
              <Pencil className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setSkillsPerson(person)} disabled={!canEdit} aria-label={`Manage skills for ${person.name}`} title="Manage Skills">
              <Sliders className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setAbsencePerson(person)} disabled={!canEdit} aria-label={`Manage absences for ${person.name}`} title="Manage Absences">
              <CalendarOff className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="sm" onClick={() => handleDelete(person.id)} disabled={!canEdit} aria-label={`Deactivate ${person.name}`} title="Deactivate Person">
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </div>
        );
      },
    },
  ];

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
    </div>
  );
}

import { useState, useEffect } from 'react';
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@foundation/src/components/ui/button';
import { Plus, Pencil, Trash2, Sliders, CalendarOff } from 'lucide-react';
import { PersonEditDialog } from './PersonEditDialog';
import { PersonSkillsEditor } from './PersonSkillsEditor';
import { PersonAbsenceList } from './PersonAbsenceList';
import { getResources, deleteResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile } from '@foundation/src/lib/api/person-profiles-api';

export function PersonList() {
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

  if (isLoading) return <div>Loading people...</div>;
  if (error) return <div>Error loading people</div>;

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={() => handleEdit(null)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Person
        </Button>
      </div>

      {people?.data?.length === 0 ? (
        <div className="text-center py-8 text-muted-foreground">
          No people found. Add your first person to get started.
        </div>
      ) : (
        <div className="border rounded-lg overflow-hidden">
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-4 font-medium">Name</th>
                <th className="text-left p-4 font-medium">Email</th>
                <th className="text-left p-4 font-medium">Job Title</th>
                <th className="text-left p-4 font-medium">Department</th>
                <th className="text-right p-4 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {people?.data?.map((person: ResourceInfo) => {
                const profile = profileByPersonId[person.id];
                return (
                <tr key={person.id} className="border-t hover:bg-accent/50">
                  <td className="p-4">{person.name}</td>
                  <td className="p-4 text-muted-foreground">{profile?.email ?? '-'}</td>
                  <td className="p-4">{profile?.jobTitleName ?? '-'}</td>
                  <td className="p-4">{profile?.departmentPath ?? '-'}</td>
                  <td className="p-4 text-right space-x-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleEdit(person)}
                      aria-label={`Edit ${person.name}`}
                      title="Edit Person"
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setSkillsPerson(person)}
                      aria-label={`Manage skills for ${person.name}`}
                      title="Manage Skills"
                    >
                      <Sliders className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setAbsencePerson(person)}
                      aria-label={`Manage absences for ${person.name}`}
                      title="Manage Absences"
                    >
                      <CalendarOff className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDelete(person.id)}
                      aria-label={`Deactivate ${person.name}`}
                      title="Deactivate Person"
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

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

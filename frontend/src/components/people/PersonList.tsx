import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { PersonEditDialog } from './PersonEditDialog';
import { getResources, deleteResource, type ResourceInfo } from '@foundation/src/lib/api/resources-api';

export function PersonList() {
  const [editingPerson, setEditingPerson] = useState<ResourceInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  const { data: people, isLoading, error } = useQuery({
    queryKey: ['resources', 'person'],
    queryFn: () => getResources({ resourceTypeKey: 'person' }),
  });

  const queryClient = useQueryClient();

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteResource(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['resources', 'person'] });
    },
  });

  const handleEdit = (person: ResourceInfo) => {
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
        <Button onClick={() => handleEdit({} as ResourceInfo)}>
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
              {people?.data?.map((person: ResourceInfo) => (
                <tr key={person.id} className="border-t hover:bg-accent/50">
                  <td className="p-4">{person.name}</td>
                  <td className="p-4 text-muted-foreground">-</td>
                  <td className="p-4">-</td>
                  <td className="p-4">-</td>
                  <td className="p-4 text-right space-x-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleEdit(person)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDelete(person.id)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </td>
                </tr>
              ))}
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
    </div>
  );
}

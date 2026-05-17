import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { Plus, Pencil, Trash2, Users, Loader2 } from 'lucide-react';
import { getResourceGroups, deleteResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { ResourceGroupEditDialog } from './ResourceGroupEditDialog';
import { ResourceGroupMembersEditor } from './ResourceGroupMembersEditor';

interface ResourceGroupListProps {
  resourceTypeKey: string;
}

export function ResourceGroupList({ resourceTypeKey }: ResourceGroupListProps) {
  const queryClient = useQueryClient();
  const [editingGroup, setEditingGroup] = useState<ResourceGroupInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [managingMembersFor, setManagingMembersFor] = useState<ResourceGroupInfo | null>(null);

  const { data: groups = [], isLoading } = useQuery({
    queryKey: ['resource-groups', resourceTypeKey],
    queryFn: () => getResourceGroups(resourceTypeKey),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteResourceGroup,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['resource-groups', resourceTypeKey] }),
  });

  const handleAdd = () => {
    setEditingGroup(null);
    setIsDialogOpen(true);
  };

  const handleEdit = (group: ResourceGroupInfo) => {
    setEditingGroup(group);
    setIsDialogOpen(true);
  };

  const handleClose = () => {
    setIsDialogOpen(false);
    setEditingGroup(null);
  };

  const handleSaved = () => {
    queryClient.invalidateQueries({ queryKey: ['resource-groups', resourceTypeKey] });
    handleClose();
  };

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={handleAdd}>
          <Plus className="h-4 w-4 mr-2" />
          Add Group
        </Button>
      </div>

      <div className="border rounded-lg overflow-hidden">
        {isLoading ? (
          <div className="flex items-center justify-center p-8">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        ) : groups.length === 0 ? (
          <div className="text-center py-8 text-muted-foreground">
            No groups yet. Click &quot;Add Group&quot; to create one.
          </div>
        ) : (
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-4 font-medium">Name</th>
                <th className="text-left p-4 font-medium">Description</th>
                <th className="text-left p-4 font-medium">Members</th>
                <th className="text-left p-4 font-medium">Default Availability</th>
                <th className="text-right p-4 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {groups.map((group) => (
                <tr key={group.id} className="border-t hover:bg-muted/50">
                  <td className="p-4 font-medium">{group.name}</td>
                  <td className="p-4 text-muted-foreground">{group.description ?? '—'}</td>
                  <td className="p-4">{group.memberCount}</td>
                  <td className="p-4">{group.defaultAvailabilityPercent}%</td>
                  <td className="p-4">
                    <div className="flex justify-end gap-2">
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => setManagingMembersFor(group)}
                        aria-label={`Manage members of ${group.name}`}
                        title="Manage members"
                      >
                        <Users className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => handleEdit(group)}
                        aria-label={`Edit ${group.name}`}
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => deleteMutation.mutate(group.id)}
                        disabled={deleteMutation.isPending}
                        aria-label={`Delete ${group.name}`}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <ResourceGroupEditDialog
        resourceTypeKey={resourceTypeKey}
        group={editingGroup}
        isOpen={isDialogOpen}
        onClose={handleClose}
        onSaved={handleSaved}
      />

      {managingMembersFor && (
        <ResourceGroupMembersEditor
          open={!!managingMembersFor}
          onOpenChange={(open) => !open && setManagingMembersFor(null)}
          groupId={managingMembersFor.id}
          groupName={managingMembersFor.name}
          resourceTypeKey={resourceTypeKey}
        />
      )}
    </div>
  );
}

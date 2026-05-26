import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { Plus, Pencil, Trash2, Users } from 'lucide-react';
import { getResourceGroups, deleteResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { ResourceGroupEditDialog } from './ResourceGroupEditDialog';
import { ResourceGroupMembersEditor } from './ResourceGroupMembersEditor';

interface ResourceGroupListProps {
  resourceTypeKey: string;
  entityLabel?: string;
}

export function ResourceGroupList({ resourceTypeKey, entityLabel = 'Group' }: ResourceGroupListProps) {
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

  const columns: ColumnDef<ResourceGroupInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ getValue }) => <span className="font-medium">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'description',
      header: 'Description',
      cell: ({ getValue }) => <span className="text-muted-foreground">{getValue<string | null>() ?? '—'}</span>,
    },
    {
      accessorKey: 'memberCount',
      header: 'Members',
    },
    {
      accessorKey: 'defaultAvailabilityPercent',
      header: 'Default Availability',
      cell: ({ getValue }) => `${getValue<number>()}%`,
    },
    {
      id: 'actions',
      header: () => null,
      size: 120,
      cell: ({ row }) => {
        const group = row.original;
        return (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" size="icon" onClick={() => setManagingMembersFor(group)} aria-label={`Manage members of ${group.name}`} title="Manage members">
              <Users className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="icon" onClick={() => handleEdit(group)} aria-label={`Edit ${group.name}`}>
              <Pencil className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="icon" onClick={() => deleteMutation.mutate(group.id)} disabled={deleteMutation.isPending} aria-label={`Delete ${group.name}`}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={handleAdd}>
          <Plus className="h-4 w-4 mr-2" />
          Add {entityLabel}
        </Button>
      </div>

      <OrkyoDataTable
        columns={columns}
        data={groups}
        isLoading={isLoading}
        emptyMessage={`No ${entityLabel.toLowerCase()}s yet. Click "Add ${entityLabel}" to create one.`}
        filterColumn="name"
        filterPlaceholder={`Search ${entityLabel.toLowerCase()}s...`}
        pageSize={25}
      />

      <ResourceGroupEditDialog
        resourceTypeKey={resourceTypeKey}
        group={editingGroup}
        isOpen={isDialogOpen}
        onClose={handleClose}
        onSaved={handleSaved}
        entityLabel={entityLabel}
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

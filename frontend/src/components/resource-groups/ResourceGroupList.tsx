import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@foundation/src/components/ui/button';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { Plus, Pencil, Trash2, Users, type LucideIcon } from 'lucide-react';
import { getResourceGroups, deleteResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { ResourceGroupEditDialog } from './ResourceGroupEditDialog';
import { ResourceGroupMembersEditor } from './ResourceGroupMembersEditor';

interface ResourceGroupListProps {
  resourceTypeKey: string;
  entityLabel?: string;
  /** Icon for the "manage members" action — reflects the member type (people, spaces, …). */
  membersIcon?: LucideIcon;
}

export function ResourceGroupList({ resourceTypeKey, entityLabel = 'Group', membersIcon: MembersIcon = Users }: ResourceGroupListProps) {
  const queryClient = useQueryClient();
  const canEdit = useCanEdit();
  const [editingGroup, setEditingGroup] = useState<ResourceGroupInfo | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [managingMembersFor, setManagingMembersFor] = useState<ResourceGroupInfo | null>(null);
  const [deletingGroup, setDeletingGroup] = useState<ResourceGroupInfo | null>(null);

  const { data: groups = [], isLoading } = useQuery({
    queryKey: qk.resourceGroups.byType(resourceTypeKey),
    queryFn: () => getResourceGroups(resourceTypeKey),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteResourceGroup,
    meta: {
      successMessage: `${entityLabel} deleted`,
      errorMessage: `Failed to delete ${entityLabel.toLowerCase()}`,
      invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    },
    onSuccess: () => setDeletingGroup(null),
  });

  const handleConfirmDelete = () => {
    if (deletingGroup) deleteMutation.mutate(deletingGroup.id);
  };

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
    queryClient.invalidateQueries({ queryKey: qk.resourceGroups.byType(resourceTypeKey) });
    handleClose();
  };

  const renderActions = (group: ResourceGroupInfo) => (
    <RowActions
      triggerLabel={`Actions for ${group.name}`}
      actions={[
        { label: 'Manage members', icon: MembersIcon, onSelect: () => setManagingMembersFor(group), disabled: !canEdit },
        { label: 'Edit', icon: Pencil, onSelect: () => handleEdit(group), disabled: !canEdit },
        { label: 'Delete', icon: Trash2, onSelect: () => setDeletingGroup(group), disabled: !canEdit, destructive: true },
      ]}
    />
  );

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
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Phone presentation: name + description/members stacked, actions trailing.
  const renderCard = (group: ResourceGroupInfo) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-0.5">
        <p className="font-medium truncate">{group.name}</p>
        <p className="text-sm text-muted-foreground truncate">{group.description ?? '—'}</p>
        <p className="text-xs text-muted-foreground truncate">
          {group.memberCount} member{group.memberCount === 1 ? '' : 's'} · {group.defaultAvailabilityPercent}% default availability
        </p>
      </div>
      {renderActions(group)}
    </div>
  );

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <Button onClick={handleAdd} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add {entityLabel}
        </Button>
      </div>

      <OrkyoDataTable
        columns={columns}
        data={groups}
        isLoading={isLoading}
        emptyMessage={`No ${entityLabel.toLowerCase()}s yet. Click "Add ${entityLabel}" to create one.`}
        emptyAction={
          <Button onClick={handleAdd} disabled={!canEdit}>
            <Plus className="h-4 w-4 mr-2" />
            Add {entityLabel}
          </Button>
        }
        filterColumn="name"
        filterPlaceholder={`Search ${entityLabel.toLowerCase()}s...`}
        pageSize={25}
        onRowClick={canEdit ? handleEdit : undefined}
        renderCard={renderCard}
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

      <ConfirmDialog
        open={!!deletingGroup}
        onOpenChange={(open) => !open && setDeletingGroup(null)}
        title={`Delete ${entityLabel.toLowerCase()}`}
        description={
          deletingGroup
            ? `Delete "${deletingGroup.name}"? This cannot be undone.`
            : ''
        }
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}

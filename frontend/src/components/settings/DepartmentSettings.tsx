import { useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Plus, Pencil, Trash2, ChevronRight, ChevronDown, AlertCircle } from 'lucide-react';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { Button } from '@foundation/src/components/ui/button';
import { Card } from '@foundation/src/components/ui/card';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { SettingsPageHeader } from './SettingsPageHeader';
import { DepartmentEditDialog } from './DepartmentEditDialog';
import {
  getDepartmentTree,
  deleteDepartment,
  type DepartmentInfo,
  type DepartmentTreeNode,
} from '@foundation/src/lib/api/departments-api';
import { qk } from '@foundation/src/lib/api/query-keys';

export function DepartmentSettings() {
  // The tree fetch returns DepartmentTreeNode (no createdAt/updatedAt).
  // The dialog only reads the structural fields, so we widen here rather than
  // refetch the full DepartmentInfo on every edit click.
  const [editing, setEditing] = useState<DepartmentInfo | DepartmentTreeNode | null>(null);
  const [createParentId, setCreateParentId] = useState<string | null | undefined>(undefined);
  // ^ undefined = dialog closed; null = create root; string = create child
  const [includeInactive, setIncludeInactive] = useState(false);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [deletingDepartment, setDeletingDepartment] = useState<DepartmentTreeNode | null>(null);

  const { data: tree = [], isLoading, error } = useQuery({
    queryKey: qk.departments.tree(includeInactive),
    queryFn: () => getDepartmentTree(includeInactive),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteDepartment(id),
    meta: {
      successMessage: 'Department deleted',
      errorMessage: 'Failed to delete department',
      invalidates: [qk.departments.all()],
    },
    onSuccess: () => setDeletingDepartment(null),
  });

  const handleDelete = (d: DepartmentTreeNode) => {
    if (d.children.length > 0) {
      toast.error('Cannot delete department', {
        description: 'Move or delete child departments first.',
      });
      return;
    }
    setDeletingDepartment(d);
  };
  const handleConfirmDelete = () => {
    if (deletingDepartment) deleteMutation.mutate(deletingDepartment.id);
  };

  const toggle = (id: string) => setExpanded((prev) => ({ ...prev, [id]: !prev[id] }));

  const renderNode = (node: DepartmentTreeNode, depth: number): React.ReactNode => {
    const isOpen = expanded[node.id] ?? true;
    const hasChildren = node.children.length > 0;
    return (
      <div key={node.id}>
        <div
          className="flex items-center gap-2 py-2 px-2 hover:bg-muted/50 rounded"
          style={{ paddingLeft: `${depth * 24 + 8}px` }}
        >
          <button
            type="button"
            onClick={() => toggle(node.id)}
            className="text-muted-foreground"
            aria-label={isOpen ? 'Collapse' : 'Expand'}
            disabled={!hasChildren}
          >
            {hasChildren ? (
              isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />
            ) : (
              <span className="inline-block w-4" />
            )}
          </button>
          <div className="flex-1 min-w-0">
            <span className="font-medium">{node.name}</span>
            {node.code && (
              <span className="ml-2 text-xs text-muted-foreground">({node.code})</span>
            )}
            {!node.isActive && <StatusBadge status="inactive" label="Inactive" className="ml-2" />}
            {node.description && (
              <p className="text-xs text-muted-foreground mt-0.5">{node.description}</p>
            )}
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setCreateParentId(node.id)}
          >
            <Plus className="h-3 w-3 mr-1" />
            Add child
          </Button>
          <Button variant="ghost" size="icon" onClick={() => setEditing(node)}>
            <Pencil className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => handleDelete(node)}
            className="text-destructive hover:text-destructive"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
        {isOpen && hasChildren && (
          <div>{node.children.map((child) => renderNode(child, depth + 1))}</div>
        )}
      </div>
    );
  };

  if (isLoading) {
    return <div className="text-muted-foreground">Loading departments...</div>;
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Departments"
        description="Tenant-wide hierarchical reference data assigned to person resources. Departments are organizational units only — they do not affect availability, capabilities, or scheduling."
      >
        <Button onClick={() => setCreateParentId(null)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Root Department
        </Button>
      </SettingsPageHeader>

      <div className="flex items-center gap-2">
        <input
          id="include-inactive-dept"
          type="checkbox"
          checked={includeInactive}
          onChange={(e) => setIncludeInactive(e.target.checked)}
        />
        <label htmlFor="include-inactive-dept" className="text-sm text-muted-foreground">
          Show inactive
        </label>
      </div>

      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription>
            {error instanceof Error ? error.message : 'Failed to load departments'}
          </AlertDescription>
        </Alert>
      )}

      {tree.length === 0 ? (
        <Card className="p-12 text-center">
          <p className="text-muted-foreground mb-4">No departments defined yet</p>
          <Button onClick={() => setCreateParentId(null)} variant="outline">
            <Plus className="h-4 w-4 mr-2" />
            Create your first department
          </Button>
        </Card>
      ) : (
        <Card className="p-2">{tree.map((node) => renderNode(node, 0))}</Card>
      )}

      {createParentId !== undefined && (
        <DepartmentEditDialog
          department={null}
          defaultParentId={createParentId}
          open={createParentId !== undefined}
          onOpenChange={(open) => !open && setCreateParentId(undefined)}
        />
      )}
      {editing && (
        <DepartmentEditDialog
          department={editing}
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}

      <ConfirmDialog
        open={!!deletingDepartment}
        onOpenChange={(open) => !open && setDeletingDepartment(null)}
        title={`Delete "${deletingDepartment?.name}"?`}
        description="People assigned to this department will be unlinked."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}

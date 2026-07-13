import { useQuery } from '@tanstack/react-query';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import {
  createDepartment,
  updateDepartment,
  getDepartmentTree,
  type DepartmentInfo,
  type DepartmentTreeNode,
} from '@foundation/src/lib/api/departments-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';

/** Structural subset shared by DepartmentInfo and DepartmentTreeNode. */
type EditableDepartment = Pick<
  DepartmentInfo,
  'id' | 'parentDepartmentId' | 'name' | 'code' | 'description' | 'isActive'
>;

interface DepartmentEditDialogProps {
  department: EditableDepartment | null;
  /** Pre-selected parent (used when "Add child" is clicked on a tree node) */
  defaultParentId?: string | null;
  /** Optional pre-populated name (used when caller opens this dialog from a "Create X" inline action). */
  initialName?: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Optional: invoked with the saved entity on successful create or update. Used by inline-create flows. */
  onSaved?: (d: DepartmentInfo) => void;
}

interface FormState {
  name: string;
  code: string;
  description: string;
  parentDepartmentId: string;  // empty = root
}

function fromInfo(d: EditableDepartment): FormState {
  return {
    name: d.name,
    code: d.code ?? '',
    description: d.description ?? '',
    parentDepartmentId: d.parentDepartmentId ?? '',
  };
}

/**
 * Flatten the department tree into depth-indented options. The currently-edited
 * department and its entire subtree are excluded to prevent the user from
 * selecting a descendant as their own parent (which would be a cycle and is
 * also rejected by the backend — this filter just keeps it out of the UI).
 */
function flattenForParentSelect(
  tree: DepartmentTreeNode[],
  excludeSubtreeRoot: string | null,
  depth = 0,
): { id: string; label: string }[] {
  const out: { id: string; label: string }[] = [];
  for (const node of tree) {
    if (node.id === excludeSubtreeRoot) continue;
    out.push({
      id: node.id,
      label: `${'  '.repeat(depth)}${node.name}`,
    });
    out.push(...flattenForParentSelect(node.children, excludeSubtreeRoot, depth + 1));
  }
  return out;
}

const ROOT_VALUE = '__root__';

export function DepartmentEditDialog({
  department,
  defaultParentId,
  initialName,
  open,
  onOpenChange,
  onSaved,
}: DepartmentEditDialogProps) {
  const { data: tree = [] } = useQuery({
    queryKey: qk.departments.tree(false),
    queryFn: () => getDepartmentTree(false),
    enabled: open,
  });

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    EditableDepartment,
    FormState,
    DepartmentInfo
  >({
    open,
    onOpenChange,
    entity: department,
    emptyForm: () => ({
      name: initialName ?? '',
      code: '',
      description: '',
      parentDepartmentId: defaultParentId ?? '',
    }),
    toForm: fromInfo,
    save: (form, dept) => {
      const parentId = form.parentDepartmentId === '' ? null : form.parentDepartmentId;
      if (dept) {
        return updateDepartment(dept.id, {
          name: form.name,
          code: form.code || undefined,
          description: form.description || undefined,
          parentDepartmentId: parentId,
          // Reparent only when the form value differs from the saved value;
          // sending changeParent=false preserves the existing parent.
          changeParent: (dept.parentDepartmentId ?? null) !== parentId,
        });
      }
      return createDepartment({
        name: form.name,
        code: form.code || undefined,
        description: form.description || undefined,
        parentDepartmentId: parentId,
      });
    },
    entityLabel: 'Department',
    invalidates: [qk.departments.all()],
    onSaved,
  });

  const handleSubmit = () => {
    if (!form.name.trim()) return;
    submit();
  };

  const parentOptions = flattenForParentSelect(tree, department?.id ?? null);

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={department ? 'Edit Department' : 'New Department'}
      description="Departments are organizational units. They do not carry capabilities, availability, or scheduling rules."
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Save"
      submitDisabled={!form.name.trim()}
      error={error}
      dirty={isDirty}
    >
      <div className="space-y-2">
        <Label htmlFor="dept-name">Name</Label>
        <Input
          id="dept-name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          maxLength={200}
          autoFocus
          required
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="dept-parent">Parent department</Label>
        <Select
          value={form.parentDepartmentId === '' ? ROOT_VALUE : form.parentDepartmentId}
          onValueChange={(v) =>
            set({ parentDepartmentId: v === ROOT_VALUE ? '' : v })
          }
        >
          <SelectTrigger id="dept-parent">
            <SelectValue placeholder="(root department)" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ROOT_VALUE}>(root department)</SelectItem>
            {parentOptions.map((opt) => (
              <SelectItem key={opt.id} value={opt.id}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor="dept-code">Code (optional)</Label>
        <Input
          id="dept-code"
          value={form.code}
          onChange={(e) => set({ code: e.target.value })}
          maxLength={50}
          placeholder="e.g. ENG, OPS"
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="dept-description">Description</Label>
        <Textarea
          id="dept-description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={3}
        />
      </div>
    </FormDialog>
  );
}

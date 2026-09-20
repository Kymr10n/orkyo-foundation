import { useState } from "react";
import { Shield } from "lucide-react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Label } from "@foundation/src/components/ui/label";
import { RoleSelect, ROLE_SUMMARIES } from "@foundation/src/components/ui/RoleSelect";
import { type UserWithRole } from "@foundation/src/lib/api/user-api";
import { useUpdateUserRole } from "@foundation/src/hooks/useTenantUsers";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";

/** Roles assignable to an existing member — every tenant role except "none". */
type EditableRole = Exclude<
  (typeof TENANT_ROLE)[keyof typeof TENANT_ROLE],
  typeof TENANT_ROLE.None
>;

const EDITABLE_ROLES: readonly EditableRole[] = [
  TENANT_ROLE.Viewer,
  TENANT_ROLE.Editor,
  TENANT_ROLE.Admin,
  TENANT_ROLE.Inactive,
];

interface EditUserRoleDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  user: UserWithRole;
  onSuccess: () => void;
}

export function EditUserRoleDialog({
  open,
  onOpenChange,
  user,
  onSuccess,
}: EditUserRoleDialogProps) {
  const [role, setRole] = useState<EditableRole>(user.role);
  const [error, setError] = useState<string | null>(null);

  const mutation = useUpdateUserRole(user.id);

  const handleSubmit = () => {
    setError(null);

    if (role === user.role) {
      setError("No changes to save");
      return;
    }

    mutation.mutate(
      { role },
      {
        onSuccess: () => {
          setError(null);
          onSuccess();
        },
        onError: (err: Error) => {
          setError(err.message);
        },
      },
    );
  };

  const handleClose = (newOpen: boolean) => {
    if (newOpen) return;
    if (!mutation.isPending) {
      setRole(user.role);
      setError(null);
      onOpenChange(false);
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={handleClose}
      title={
        <span className="flex items-center gap-2">
          <Shield className="h-5 w-5" />
          Edit User Role
        </span>
      }
      description={`Change the role and permissions for ${user.displayName} (${user.email})`}
      onSubmit={handleSubmit}
      isSubmitting={mutation.isPending}
      submitLabel="Save Changes"
      error={error}
      dirty={role !== user.role}
    >
      {/* Current Role Info */}
      <div className="rounded-lg bg-muted p-3 text-sm">
        <p className="text-muted-foreground">
          Current role: <span className="font-medium">{user.role}</span>
        </p>
      </div>

      {/* Role Field */}
      <div className="space-y-2">
        <Label htmlFor="role">New Role</Label>
        <RoleSelect
          id="role"
          value={role}
          onValueChange={setRole}
          roles={EDITABLE_ROLES}
          withDescriptions
          disabled={mutation.isPending}
          className="w-full"
        />
      </div>

      {/* Role Description */}
      <div className="rounded-lg bg-muted p-3 text-sm">
        <p className="text-muted-foreground">{ROLE_SUMMARIES[role]}</p>
      </div>

      {/* Warning for sensitive changes */}
      {(role === TENANT_ROLE.Admin || role === TENANT_ROLE.Inactive) && role !== user.role && (
        <div className="rounded-lg border border-yellow-500/50 bg-yellow-500/10 p-3 text-sm">
          <p className="text-yellow-700 dark:text-yellow-400">
            {role === TENANT_ROLE.Admin &&
              "⚠️ This will grant full administrative access including the ability to manage other users."}
            {role === TENANT_ROLE.Inactive &&
              "⚠️ This will immediately revoke all access for this user."}
          </p>
        </div>
      )}
    </FormDialog>
  );
}

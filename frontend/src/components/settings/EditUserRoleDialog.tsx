import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Shield } from "lucide-react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { Label } from "@foundation/src/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import {
  updateUserRole,
  type UserWithRole,
  type UpdateUserRoleRequest,
} from "@foundation/src/lib/api/user-api";

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
  const [role, setRole] = useState<
    "admin" | "editor" | "viewer" | "inactive"
  >(user.role);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (data: UpdateUserRoleRequest) =>
      updateUserRole(user.id, data),
    meta: {
      successMessage: "User role updated",
      errorMessage: "Failed to update user role",
      invalidates: [["users"]],
    },
    onSuccess: () => {
      setError(null);
      onSuccess();
    },
    onError: (err: Error) => {
      setError(err.message);
    },
  });

  const handleSubmit = () => {
    setError(null);

    if (role === user.role) {
      setError("No changes to save");
      return;
    }

    mutation.mutate({ role });
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
        <Select
          value={role}
          onValueChange={(value: "admin" | "editor" | "viewer" | "inactive") => setRole(value)}
          disabled={mutation.isPending}
        >
          <SelectTrigger id="role">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="viewer">
              <div>
                <div className="font-medium">Viewer</div>
                <div className="text-xs text-muted-foreground">
                  Can view data but cannot make changes
                </div>
              </div>
            </SelectItem>
            <SelectItem value="editor">
              <div>
                <div className="font-medium">Editor</div>
                <div className="text-xs text-muted-foreground">
                  Can create and modify utilization and requests
                </div>
              </div>
            </SelectItem>
            <SelectItem value="admin">
              <div>
                <div className="font-medium">Admin</div>
                <div className="text-xs text-muted-foreground">
                  Full access including settings and user management
                </div>
              </div>
            </SelectItem>
            <SelectItem value="inactive">
              <div>
                <div className="font-medium">Inactive</div>
                <div className="text-xs text-muted-foreground">
                  No access - user account disabled
                </div>
              </div>
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Role Description */}
      <div className="rounded-lg bg-muted p-3 text-sm">
        <p className="text-muted-foreground">
          {role === "admin" &&
            "Admins have full access to all features including user management and settings."}
          {role === "editor" &&
            "Editors can create and modify utilization, requests, and spaces but cannot access settings."}
          {role === "viewer" &&
            "Viewers have read-only access to view utilization and plans."}
          {role === "inactive" &&
            "Inactive users cannot log in and have no access to the system."}
        </p>
      </div>

      {/* Warning for sensitive changes */}
      {(role === "admin" || role === "inactive") && role !== user.role && (
        <div className="rounded-lg border border-yellow-500/50 bg-yellow-500/10 p-3 text-sm">
          <p className="text-yellow-700 dark:text-yellow-400">
            {role === "admin" &&
              "⚠️ This will grant full administrative access including the ability to manage other users."}
            {role === "inactive" &&
              "⚠️ This will immediately revoke all access for this user."}
          </p>
        </div>
      )}
    </FormDialog>
  );
}

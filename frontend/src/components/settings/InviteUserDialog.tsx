import { useState } from "react";
import { Mail } from "lucide-react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { RoleSelect, ROLE_SUMMARIES } from "@foundation/src/components/ui/RoleSelect";
import { useCreateInvitation } from "@foundation/src/hooks/useTenantUsers";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";
import { isValidEmail } from "@foundation/src/lib/utils/validation";

/** Roles a new invitee can be granted — every tenant role except "none" and "inactive". */
type InvitableRole = Exclude<
  (typeof TENANT_ROLE)[keyof typeof TENANT_ROLE],
  typeof TENANT_ROLE.None | typeof TENANT_ROLE.Inactive
>;

const INVITABLE_ROLES: readonly InvitableRole[] = [
  TENANT_ROLE.Viewer,
  TENANT_ROLE.Editor,
  TENANT_ROLE.Admin,
];

interface InviteUserDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}

export function InviteUserDialog({
  open,
  onOpenChange,
  onSuccess,
}: InviteUserDialogProps) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<InvitableRole>(TENANT_ROLE.Viewer);
  const [error, setError] = useState<string | null>(null);

  const mutation = useCreateInvitation();

  const handleSubmit = () => {
    setError(null);

    if (!email.trim()) {
      setError("Email is required");
      return;
    }

    if (!isValidEmail(email.trim())) {
      setError("Please enter a valid email address");
      return;
    }

    mutation.mutate(
      { email: email.trim(), role },
      {
        onSuccess: () => {
          setEmail("");
          setRole(TENANT_ROLE.Viewer);
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
      setEmail("");
      setRole(TENANT_ROLE.Viewer);
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
          <Mail className="h-5 w-5" />
          Invite User
        </span>
      }
      description="Send an invitation email to add a new user to your workspace. They'll receive a link to set up their account."
      onSubmit={handleSubmit}
      isSubmitting={mutation.isPending}
      submitLabel="Send Invitation"
      submittingLabel="Sending..."
      error={error}
      dirty={!!email.trim() || role !== TENANT_ROLE.Viewer}
    >
      {/* Email Field */}
      <FormField htmlFor="email" label="Email Address" required>
        <Input
          id="email"
          type="email"
          placeholder="colleague@example.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          disabled={mutation.isPending}
          autoFocus
        />
      </FormField>

      {/* Role Field */}
      <div className="space-y-2">
        <Label htmlFor="role">Role</Label>
        <RoleSelect
          id="role"
          value={role}
          onValueChange={setRole}
          roles={INVITABLE_ROLES}
          withDescriptions
          disabled={mutation.isPending}
          className="w-full"
        />
      </div>

      {/* Role Description */}
      <div className="rounded-lg bg-muted p-3 text-sm">
        <p className="text-muted-foreground">{ROLE_SUMMARIES[role]}</p>
      </div>
    </FormDialog>
  );
}

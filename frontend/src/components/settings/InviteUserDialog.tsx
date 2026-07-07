import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Mail } from "lucide-react";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { FormField } from "@foundation/src/components/ui/FormField";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { createInvitation, type CreateInvitationRequest } from "@foundation/src/lib/api/user-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";
import { isValidEmail } from "@foundation/src/lib/utils/validation";

/** Roles a new invitee can be granted — every tenant role except "none" and "inactive". */
type InvitableRole = Exclude<
  (typeof TENANT_ROLE)[keyof typeof TENANT_ROLE],
  typeof TENANT_ROLE.None | typeof TENANT_ROLE.Inactive
>;

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

  const mutation = useMutation({
    mutationFn: (data: CreateInvitationRequest) => createInvitation(data),
    meta: {
      successMessage: "Invitation sent",
      errorMessage: "Failed to send invitation",
      invalidates: [qk.invitations.all()],
    },
    onSuccess: () => {
      setEmail("");
      setRole(TENANT_ROLE.Viewer);
      setError(null);
      onSuccess();
    },
    onError: (err: Error) => {
      setError(err.message);
    },
  });

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

    mutation.mutate({ email: email.trim(), role });
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
        <Select
          value={role}
          onValueChange={(value: InvitableRole) => setRole(value)}
          disabled={mutation.isPending}
        >
          <SelectTrigger id="role">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={TENANT_ROLE.Viewer}>
              <div>
                <div className="font-medium">Viewer</div>
                <div className="text-xs text-muted-foreground">
                  Can view data but cannot make changes
                </div>
              </div>
            </SelectItem>
            <SelectItem value={TENANT_ROLE.Editor}>
              <div>
                <div className="font-medium">Editor</div>
                <div className="text-xs text-muted-foreground">
                  Can create and modify utilization and requests
                </div>
              </div>
            </SelectItem>
            <SelectItem value={TENANT_ROLE.Admin}>
              <div>
                <div className="font-medium">Admin</div>
                <div className="text-xs text-muted-foreground">
                  Full access including settings and user management
                </div>
              </div>
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Role Description */}
      <div className="rounded-lg bg-muted p-3 text-sm">
        <p className="text-muted-foreground">
          {role === TENANT_ROLE.Admin &&
            "Admins have full access to all features including user management and settings."}
          {role === TENANT_ROLE.Editor &&
            "Editors can create and modify utilization, requests, and spaces but cannot access settings."}
          {role === TENANT_ROLE.Viewer &&
            "Viewers have read-only access to view utilization and plans."}
        </p>
      </div>
    </FormDialog>
  );
}

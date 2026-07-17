import { useState } from "react";
import { SettingsPageHeader } from "./SettingsPageHeader";
import {
  Plus,
  Edit,
  Trash2,
  AlertCircle,
  Mail,
  Shield,
  X,
  RefreshCw,
} from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import { Badge } from "@foundation/src/components/ui/badge";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { useQuery, useMutation } from "@tanstack/react-query";
import {
  getUsers,
  getInvitations,
  cancelInvitation,
  resendInvitation,
  deleteUser,
  createInvitation,
  type UserWithRole,
  type Invitation,
} from "@foundation/src/lib/api/user-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";
import { InviteUserDialog } from "./InviteUserDialog";
import { EditUserRoleDialog } from "./EditUserRoleDialog";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { useExportHandler, useImportHandler } from '@foundation/src/hooks/useImportExport';
import { exportUsers, importUsers } from '@foundation/src/lib/utils/export-handlers';
import { logger } from '@foundation/src/lib/core/logger';
import { formatDateDisplay } from '@foundation/src/lib/formatters';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';

export function UserSettings() {
  const [inviteDialogOpen, setInviteDialogOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserWithRole | null>(null);
  const [cancelingInvitation, setCancelingInvitation] = useState<Invitation | null>(null);
  const [deletingUser, setDeletingUser] = useState<UserWithRole | null>(null);

  // Load users
  const {
    data: users = [],
    isLoading: usersLoading,
    error: usersError,
    refetch: refetchUsers,
  } = useQuery({
    queryKey: qk.users.all(),
    queryFn: getUsers,
  });

  // Load invitations
  const {
    data: invitations = [],
    isLoading: invitationsLoading,
    error: invitationsError,
    refetch: refetchInvitations,
  } = useQuery({
    queryKey: qk.invitations.all(),
    queryFn: getInvitations,
  });

  // Cancel invitation mutation
  const cancelMutation = useMutation({
    mutationFn: cancelInvitation,
    meta: {
      errorMessage: "Failed to cancel invitation",
      invalidates: [qk.invitations.all()],
    },
    onSuccess: () => setCancelingInvitation(null),
  });

  // Resend invitation mutation
  const resendMutation = useMutation({
    mutationFn: resendInvitation,
    meta: {
      successMessage: "Invitation email resent successfully",
      errorMessage: "Failed to resend invitation",
    },
  });

  // Delete user mutation
  const deleteMutation = useMutation({
    mutationFn: deleteUser,
    meta: {
      errorMessage: "Failed to remove user",
      invalidates: [qk.users.all()],
    },
    onSuccess: () => setDeletingUser(null),
  });

  // Handle export/import
  useExportHandler('users', async (format) => {
    await exportUsers(users, format);
    logger.info(`Exported ${users.length} users as ${format.toUpperCase()}`);
  });

  useImportHandler(
    'users',
    async (file, format) => {
      const importedUsers = await importUsers(file, format);
      if (!importedUsers.length) {
        throw new Error('No valid users found in file');
      }
      // Invite users via API
      for (const user of importedUsers) {
        if (!user.email) continue;
        const role = user.role === TENANT_ROLE.Inactive ? TENANT_ROLE.Viewer : (user.role || TENANT_ROLE.Viewer);
        await createInvitation({
          email: user.email,
          role,
        });
      }
      return importedUsers.length;
    },
    {
      successMessage: (n) => `Successfully imported ${n} users`,
      errorMessage: 'Failed to import users',
      invalidates: [qk.users.all(), qk.invitations.all()],
    },
  );

  const handleCancelInvitation = (invitation: Invitation) => setCancelingInvitation(invitation);
  const handleConfirmCancelInvitation = () => {
    if (!cancelingInvitation) return;
    cancelMutation.mutate(cancelingInvitation.id);
  };

  const handleResendInvitation = (invitation: Invitation) => {
    resendMutation.mutate(invitation.id);
  };

  const handleDeleteUser = (user: UserWithRole) => setDeletingUser(user);
  const handleConfirmDeleteUser = () => {
    if (!deletingUser) return;
    deleteMutation.mutate(deletingUser.id);
  };

  const getRoleBadgeColor = (role: string) => {
    switch (role) {
      case "admin":
        return "bg-red-500/10 text-red-700 dark:text-red-400 border-red-500/20";
      case "editor":
        return "bg-blue-500/10 text-blue-700 dark:text-blue-400 border-blue-500/20";
      case "viewer":
        return "bg-green-500/10 text-green-700 dark:text-green-400 border-green-500/20";
      case "inactive":
        return "bg-gray-500/10 text-gray-700 dark:text-gray-400 border-gray-500/20";
      default:
        return "bg-gray-500/10 text-gray-700 dark:text-gray-400 border-gray-500/20";
    }
  };

  const getStatusBadgeColor = (status: string) => {
    switch (status) {
      case "active":
        return "bg-green-500/10 text-green-700 dark:text-green-400";
      case "pending":
        return "bg-yellow-500/10 text-yellow-700 dark:text-yellow-400";
      case "suspended":
        return "bg-red-500/10 text-red-700 dark:text-red-400";
      default:
        return "bg-gray-500/10 text-gray-700 dark:text-gray-400";
    }
  };

  // Shared row actions — desktop table cell and phone card.
  const renderInvitationActions = (invitation: Invitation) => (
    <div className="flex justify-end gap-1">
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); handleResendInvitation(invitation); }}
        disabled={resendMutation.isPending}
        title="Resend invitation"
        aria-label={`Resend invitation to ${invitation.email}`}
      >
        <RefreshCw className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); handleCancelInvitation(invitation); }}
        disabled={cancelMutation.isPending}
        className="text-destructive hover:text-destructive"
        title="Cancel invitation"
        aria-label={`Cancel invitation for ${invitation.email}`}
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  );

  const renderInvitationCard = (invitation: Invitation) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <Mail className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="font-medium truncate">{invitation.email}</span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="outline" className={getRoleBadgeColor(invitation.role)}>
            {invitation.role}
          </Badge>
          <span className="text-xs text-muted-foreground">
            Expires {formatDateDisplay(invitation.expiresAt)}
          </span>
        </div>
      </div>
      {renderInvitationActions(invitation)}
    </div>
  );

  const invitationColumns: ColumnDef<Invitation>[] = [
    {
      accessorKey: 'email',
      header: 'Email',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <Mail className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="font-medium">{row.original.email}</span>
        </div>
      ),
    },
    {
      id: 'role',
      header: 'Role',
      cell: ({ row }) => (
        <Badge variant="outline" className={getRoleBadgeColor(row.original.role)}>
          {row.original.role}
        </Badge>
      ),
    },
    {
      id: 'sent',
      header: 'Sent',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {formatDateDisplay(row.original.createdAt)}
        </span>
      ),
    },
    {
      id: 'expires',
      header: 'Expires',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {formatDateDisplay(row.original.expiresAt)}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => renderInvitationActions(row.original),
    },
  ];

  const renderUserActions = (user: UserWithRole) => (
    <div className="flex justify-end gap-1">
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); setEditingUser(user); }}
        title="Edit user role"
        aria-label={`Edit ${user.displayName}`}
      >
        <Edit className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        onClick={(e) => { e.stopPropagation(); handleDeleteUser(user); }}
        disabled={deleteMutation.isPending}
        className="text-destructive hover:text-destructive"
        title="Remove user"
        aria-label={`Remove ${user.displayName}`}
      >
        <Trash2 className="h-4 w-4 text-destructive" />
      </Button>
    </div>
  );

  const renderUserCard = (user: UserWithRole) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <Shield className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="font-semibold truncate">{user.displayName}</span>
        </div>
        <p className="text-sm text-muted-foreground truncate">{user.email}</p>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="outline" className={getRoleBadgeColor(user.role)}>{user.role}</Badge>
          <Badge className={getStatusBadgeColor(user.status)}>{user.status}</Badge>
        </div>
      </div>
      {renderUserActions(user)}
    </div>
  );

  const userColumns: ColumnDef<UserWithRole>[] = [
    {
      accessorKey: 'email',
      header: 'User',
      cell: ({ row }) => (
        <div className="flex items-center gap-3">
          <Shield className="h-4 w-4 text-muted-foreground shrink-0" />
          <div>
            <p className="font-semibold">{row.original.displayName}</p>
            <p className="text-sm text-muted-foreground">{row.original.email}</p>
          </div>
        </div>
      ),
    },
    {
      id: 'role',
      header: 'Role',
      cell: ({ row }) => (
        <Badge variant="outline" className={getRoleBadgeColor(row.original.role)}>
          {row.original.role}
        </Badge>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge className={getStatusBadgeColor(row.original.status)}>
          {row.original.status}
        </Badge>
      ),
    },
    {
      id: 'created',
      header: 'Created',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {formatDateDisplay(row.original.createdAt)}
        </span>
      ),
    },
    {
      id: 'lastLogin',
      header: 'Last Login',
      cell: ({ row }) => (
        <span className="text-xs text-muted-foreground">
          {row.original.lastLoginAt ? formatDateDisplay(row.original.lastLoginAt) : '—'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 96,
      cell: ({ row }) => renderUserActions(row.original),
    },
  ];

  const isLoading = usersLoading || invitationsLoading;
  const error = usersError || invitationsError;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading users…</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <SettingsPageHeader
        title="User Management"
        description="Manage user access and roles. Invite new users, update permissions, and monitor user status."
      >
        <Button onClick={() => setInviteDialogOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Invite User
        </Button>
      </SettingsPageHeader>

      {/* Error State */}
      {error && (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{error instanceof Error ? error.message : "Failed to load users"}</span>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                refetchUsers();
                refetchInvitations();
              }}
            >
              Try again
            </Button>
          </AlertDescription>
        </Alert>
      )}

      {/* Pending Invitations Section */}
      {invitations.length > 0 && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold flex items-center gap-2">
            <Mail className="h-4 w-4" />
            Pending Invitations ({invitations.length})
          </h3>
          <OrkyoDataTable
            columns={invitationColumns}
            data={invitations}
            emptyMessage="No pending invitations."
            renderCard={renderInvitationCard}
          />
        </div>
      )}

      {/* Active Users Section */}
      <div className="space-y-3">
        {users.length === 0 ? (
          <EmptyState
            message="No users yet"
            action={
              <Button onClick={() => setInviteDialogOpen(true)} variant="outline">
                <Plus className="h-4 w-4 mr-2" />
                Invite your first user
              </Button>
            }
          />
        ) : (
          <OrkyoDataTable
            columns={userColumns}
            data={users}
            filterColumn="email"
            filterPlaceholder="Search users..."
            onRowClick={(user) => setEditingUser(user)}
            renderCard={renderUserCard}
          />
        )}
      </div>

      {/* Dialogs */}
      <InviteUserDialog
        open={inviteDialogOpen}
        onOpenChange={setInviteDialogOpen}
        // Invalidation is owned by the dialog's meta.invalidates (central MutationCache).
        onSuccess={() => setInviteDialogOpen(false)}
      />

      {editingUser && (
        <EditUserRoleDialog
          open={!!editingUser}
          onOpenChange={(open) => !open && setEditingUser(null)}
          user={editingUser}
          // Invalidation is owned by the dialog's meta.invalidates (central MutationCache).
          onSuccess={() => setEditingUser(null)}
        />
      )}

      <ConfirmDialog
        open={!!cancelingInvitation}
        onOpenChange={(open) => !open && setCancelingInvitation(null)}
        title={`Cancel invitation for "${cancelingInvitation?.email}"?`}
        description="This action cannot be undone."
        confirmLabel="Cancel Invitation"
        destructive
        isPending={cancelMutation.isPending}
        onConfirm={handleConfirmCancelInvitation}
      />

      <ConfirmDialog
        open={!!deletingUser}
        onOpenChange={(open) => !open && setDeletingUser(null)}
        title={`Remove "${deletingUser?.displayName}"?`}
        description="This will set their role to inactive."
        confirmLabel="Remove"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDeleteUser}
      />
    </div>
  );
}

import { useState } from "react";
import { SettingsPageHeader } from "./SettingsPageHeader";
import {
  Plus,
  Edit,
  Trash2,
  AlertCircle,
  Mail,
  Shield,
  Clock,
  X,
  RefreshCw,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  getUsers,
  getInvitations,
  cancelInvitation,
  resendInvitation,
  deleteUser,
  createInvitation,
  type UserWithRole,
  type Invitation,
} from "@/lib/api/user-api";
import { InviteUserDialog } from "./InviteUserDialog";
import { EditUserRoleDialog } from "./EditUserRoleDialog";
import { useExportHandler, useImportHandler } from '@/hooks/useImportExport';
import { exportUsers, importUsers } from '@/lib/utils/export-handlers';
import { logger } from '@/lib/core/logger';

export function UserSettings() {
  const queryClient = useQueryClient();
  const [inviteDialogOpen, setInviteDialogOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserWithRole | null>(null);

  // Load users
  const {
    data: users = [],
    isLoading: usersLoading,
    error: usersError,
    refetch: refetchUsers,
  } = useQuery({
    queryKey: ["users"],
    queryFn: getUsers,
  });

  // Load invitations
  const {
    data: invitations = [],
    isLoading: invitationsLoading,
    error: invitationsError,
    refetch: refetchInvitations,
  } = useQuery({
    queryKey: ["invitations"],
    queryFn: getInvitations,
  });

  // Cancel invitation mutation
  const cancelMutation = useMutation({
    mutationFn: cancelInvitation,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["invitations"] });
    },
  });

  // Resend invitation mutation
  const resendMutation = useMutation({
    mutationFn: resendInvitation,
    onSuccess: () => {
      alert("Invitation email resent successfully");
    },
  });

  // Delete user mutation
  const deleteMutation = useMutation({
    mutationFn: deleteUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
  });

  // Handle export/import
  useExportHandler('users', async (format) => {
    await exportUsers(users, format);
    logger.info(`Exported ${users.length} users as ${format.toUpperCase()}`);
  });

  useImportHandler('users', async (file, format) => {
    try {
      const importedUsers = await importUsers(file, format);
      if (!importedUsers.length) {
        throw new Error('No valid users found in file');
      }
      // Invite users via API
      for (const user of importedUsers) {
        if (!user.email) continue;
        const role = user.role === 'inactive' ? 'viewer' : (user.role || 'viewer');
        await createInvitation({
          email: user.email,
          role,
        });
      }
      // Reload users and invitations
      queryClient.invalidateQueries({ queryKey: ["users"] });
      queryClient.invalidateQueries({ queryKey: ["invitations"] });
      alert(`Successfully imported ${importedUsers.length} users`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import users');
    }
  });

  const handleCancelInvitation = async (invitation: Invitation) => {
    if (
      !confirm(
        `Cancel invitation for ${invitation.email}? This action cannot be undone.`
      )
    ) {
      return;
    }

    try {
      await cancelMutation.mutateAsync(invitation.id);
    } catch (err) {
      alert(
        err instanceof Error ? err.message : "Failed to cancel invitation"
      );
    }
  };

  const handleResendInvitation = async (invitation: Invitation) => {
    try {
      await resendMutation.mutateAsync(invitation.id);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to resend invitation");
    }
  };

  const handleDeleteUser = async (user: UserWithRole) => {
    if (
      !confirm(
        `Remove ${user.displayName} (${user.email})? This will set their role to inactive.`
      )
    ) {
      return;
    }

    try {
      await deleteMutation.mutateAsync(user.id);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to remove user");
    }
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

  const isLoading = usersLoading || invitationsLoading;
  const error = usersError || invitationsError;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-muted-foreground">Loading users...</p>
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
        <div className="flex items-center gap-2 p-4 border border-destructive/50 bg-destructive/10 rounded-lg">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">
            {error instanceof Error ? error.message : "Failed to load users"}
          </p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              refetchUsers();
              refetchInvitations();
            }}
            className="ml-auto"
          >
            Retry
          </Button>
        </div>
      )}

      {/* Pending Invitations Section */}
      {invitations.length > 0 && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold flex items-center gap-2">
            <Mail className="h-4 w-4" />
            Pending Invitations ({invitations.length})
          </h3>
          <div className="grid gap-3">
            {invitations.map((invitation) => (
              <Card key={invitation.id} className="p-4 bg-muted/50">
                <div className="flex items-start justify-between">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-3 mb-2">
                      <Mail className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                      <span className="font-medium">{invitation.email}</span>
                      <Badge
                        variant="outline"
                        className={getRoleBadgeColor(invitation.role)}
                      >
                        {invitation.role}
                      </Badge>
                    </div>

                    <div className="flex items-center gap-4 text-xs text-muted-foreground ml-7">
                      <span>
                        <Clock className="h-3 w-3 inline mr-1" />
                        Expires:{" "}
                        {new Date(invitation.expiresAt).toLocaleDateString()}
                      </span>
                      <span>
                        Sent: {new Date(invitation.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                  </div>

                  <div className="flex gap-2 ml-4">
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleResendInvitation(invitation)}
                      disabled={resendMutation.isPending}
                      title="Resend invitation"
                    >
                      <RefreshCw className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleCancelInvitation(invitation)}
                      disabled={cancelMutation.isPending}
                      className="text-destructive hover:text-destructive"
                      title="Cancel invitation"
                    >
                      <X className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        </div>
      )}

      {/* Active Users Section */}
      <div className="space-y-3">
        {users.length === 0 ? (
          <Card className="p-12 text-center">
            <p className="text-muted-foreground mb-4">No users yet</p>
            <Button onClick={() => setInviteDialogOpen(true)} variant="outline">
              <Plus className="h-4 w-4 mr-2" />
              Invite your first user
            </Button>
          </Card>
        ) : (
          <div className="grid gap-3">
            {users.map((user) => (
              <Card key={user.id} className="p-4">
                <div className="flex items-start justify-between">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-3 mb-2">
                      <Shield className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                      <div>
                        <h3 className="font-semibold">{user.displayName}</h3>
                        <p className="text-sm text-muted-foreground">
                          {user.email}
                        </p>
                      </div>
                      <Badge
                        variant="outline"
                        className={getRoleBadgeColor(user.role)}
                      >
                        {user.role}
                      </Badge>
                      <Badge className={getStatusBadgeColor(user.status)}>
                        {user.status}
                      </Badge>
                    </div>

                    <div className="flex items-center gap-4 text-xs text-muted-foreground ml-7">
                      <span>
                        Created: {new Date(user.createdAt).toLocaleDateString()}
                      </span>
                      {user.lastLoginAt && (
                        <span>
                          Last login:{" "}
                          {new Date(user.lastLoginAt).toLocaleDateString()}
                        </span>
                      )}
                    </div>
                  </div>

                  <div className="flex gap-2 ml-4">
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => setEditingUser(user)}
                      title="Edit user role"
                    >
                      <Edit className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleDeleteUser(user)}
                      disabled={deleteMutation.isPending}
                      className="text-destructive hover:text-destructive"
                      title="Remove user"
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>

      {/* Dialogs */}
      <InviteUserDialog
        open={inviteDialogOpen}
        onOpenChange={setInviteDialogOpen}
        onSuccess={() => {
          setInviteDialogOpen(false);
          queryClient.invalidateQueries({ queryKey: ["invitations"] });
        }}
      />

      {editingUser && (
        <EditUserRoleDialog
          open={!!editingUser}
          onOpenChange={(open) => !open && setEditingUser(null)}
          user={editingUser}
          onSuccess={() => {
            setEditingUser(null);
            queryClient.invalidateQueries({ queryKey: ["users"] });
          }}
        />
      )}
    </div>
  );
}

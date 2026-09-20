import { useMutation, useQuery } from "@tanstack/react-query";
import {
  cancelInvitation,
  createInvitation,
  deleteUser,
  getInvitations,
  getUsers,
  resendInvitation,
  updateUserRole,
  type CreateInvitationRequest,
  type UpdateUserRoleRequest,
} from "@foundation/src/lib/api/user-api";
import { qk } from "@foundation/src/lib/api/query-keys";

export const useUsers = () =>
  useQuery({
    queryKey: qk.users.all(),
    queryFn: getUsers,
  });

export const useInvitations = () =>
  useQuery({
    queryKey: qk.invitations.all(),
    queryFn: getInvitations,
  });

export const useCreateInvitation = () =>
  useMutation({
    mutationFn: (data: CreateInvitationRequest) => createInvitation(data),
    meta: {
      successMessage: "Invitation sent",
      suppressErrorToast: true,
      invalidates: [qk.invitations.all()],
    },
  });

export const useCancelInvitation = () =>
  useMutation({
    mutationFn: cancelInvitation,
    meta: {
      errorMessage: "Failed to cancel invitation",
      invalidates: [qk.invitations.all()],
    },
  });

export const useResendInvitation = () =>
  useMutation({
    mutationFn: resendInvitation,
    meta: {
      successMessage: "Invitation email resent successfully",
      errorMessage: "Failed to resend invitation",
    },
  });

export const useUpdateUserRole = (userId: string) =>
  useMutation({
    mutationFn: (data: UpdateUserRoleRequest) => updateUserRole(userId, data),
    meta: {
      successMessage: "User role updated",
      suppressErrorToast: true,
      invalidates: [qk.users.all()],
    },
  });

export const useDeleteUser = () =>
  useMutation({
    mutationFn: deleteUser,
    meta: {
      errorMessage: "Failed to remove user",
      invalidates: [qk.users.all()],
    },
  });

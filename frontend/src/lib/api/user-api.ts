import { apiGet, apiPost, apiDelete, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface UserWithRole {
  id: string;
  email: string;
  displayName: string;
  role: "admin" | "editor" | "viewer" | "inactive";
  status: "pending" | "active" | "suspended";
  createdAt: string;
  lastLoginAt?: string;
}

export interface Invitation {
  id: string;
  email: string;
  role: "admin" | "editor" | "viewer";
  invitedBy: string;
  tokenHash: string;
  expiresAt: string;
  acceptedAt?: string;
  createdAt: string;
}

export interface CreateInvitationRequest {
  email: string;
  role: "admin" | "editor" | "viewer";
}

export interface UpdateUserRoleRequest {
  role: "admin" | "editor" | "viewer" | "inactive";
}

/**
 * Get all users in the tenant
 */
export async function getUsers(): Promise<UserWithRole[]> {
  const data = await apiGet<{ users: UserWithRole[] }>(API_PATHS.USERS);
  return data.users || [];
}

/**
 * Get all pending invitations
 */
export async function getInvitations(): Promise<Invitation[]> {
  const data = await apiGet<{ invitations: Invitation[] }>(API_PATHS.USER_INVITATIONS);
  return data.invitations || [];
}

/**
 * Create a new user invitation
 */
export async function createInvitation(
  data: CreateInvitationRequest
): Promise<Invitation> {
  return apiPost<Invitation>(API_PATHS.USER_INVITE, data);
}

/**
 * Resend an invitation email
 */
export async function resendInvitation(invitationId: string): Promise<void> {
  await apiPost<void>(API_PATHS.userInvitationResend(invitationId), undefined);
}

/**
 * Cancel/delete an invitation
 */
export async function cancelInvitation(invitationId: string): Promise<void> {
  return apiDelete(API_PATHS.userInvitation(invitationId));
}

/**
 * Update a user's role
 */
export async function updateUserRole(
  userId: string,
  data: UpdateUserRoleRequest
): Promise<UserWithRole> {
  return apiPatch<UserWithRole>(API_PATHS.userRole(userId), data);
}

/**
 * Delete a user (soft delete - sets role to inactive)
 */
export async function deleteUser(userId: string): Promise<void> {
  return apiDelete(API_PATHS.user(userId));
}

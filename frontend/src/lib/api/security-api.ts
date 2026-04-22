import { apiGet, apiPost, apiPut, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

/**
 * Security info about the current user
 */
interface SecurityInfo {
  isFederated: boolean;
  identityProvider?: string;
  canChangePassword: boolean;
}

/**
 * Active session information
 */
interface Session {
  id: string;
  ipAddress: string;
  startTime: string;
  lastAccessTime: string;
  isCurrent: boolean;
  clients: string[];
}

/**
 * Request to change password
 */
interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

/**
 * MFA status for the current user
 */
interface MfaStatus {
  totpEnabled: boolean;
  totpCredentialId?: string;
  totpCreatedDate?: string;
  totpLabel?: string;
  recoveryCodesConfigured: boolean;
}

/**
 * User profile from identity provider
 */
interface UserProfileData {
  email: string;
  firstName: string;
  lastName: string;
  emailVerified: boolean;
}

/**
 * Request to update user profile
 */
interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
}

/**
 * Get security information for the current user
 * This tells us if the user can change their password
 * (federated users manage passwords in their IdP)
 */
export async function getSecurityInfo(): Promise<SecurityInfo> {
  return apiGet<SecurityInfo>(API_PATHS.ACCOUNT.SECURITY_INFO);
}

/**
 * Get all active sessions for the current user
 */
export async function getSessions(): Promise<Session[]> {
  return apiGet<Session[]>(API_PATHS.ACCOUNT.SESSIONS);
}

/**
 * Change the user's password
 * Only works for non-federated users
 */
export async function changePassword(
  data: ChangePasswordRequest
): Promise<{ message: string }> {
  return apiPost<{ message: string }>(
    API_PATHS.ACCOUNT.PASSWORD,
    data
  );
}

/**
 * Revoke a specific session
 */
export async function revokeSession(sessionId: string): Promise<void> {
  return apiDelete(API_PATHS.ACCOUNT.session(sessionId));
}

/**
 * Log out from all sessions (including current)
 */
export async function logoutAllSessions(): Promise<{ message: string }> {
  return apiPost<{ message: string }>(API_PATHS.ACCOUNT.LOGOUT_ALL, undefined);
}

/**
 * Get MFA status for the current user
 */
export async function getMfaStatus(): Promise<MfaStatus> {
  return apiGet<MfaStatus>(API_PATHS.ACCOUNT.MFA_STATUS);
}

/**
 * Remove MFA (TOTP + recovery codes). User will be re-prompted on next login.
 */
export async function removeMfa(): Promise<void> {
  return apiDelete(API_PATHS.ACCOUNT.MFA);
}

/**
 * Enable MFA enrollment. Adds CONFIGURE_TOTP required action in Keycloak.
 * User will be prompted to set up TOTP on their next login.
 */
export async function enableMfa(): Promise<{ message: string }> {
  return apiPost<{ message: string }>(API_PATHS.ACCOUNT.MFA, undefined);
}

/**
 * Get user profile from identity provider
 */
export async function getUserProfile(): Promise<UserProfileData> {
  return apiGet<UserProfileData>(API_PATHS.ACCOUNT.PROFILE);
}

/**
 * Update user profile (first name, last name)
 */
export async function updateUserProfile(
  data: UpdateProfileRequest
): Promise<{ message: string; displayName: string }> {
  return apiPut<{ message: string; displayName: string }>(
    API_PATHS.ACCOUNT.PROFILE,
    data
  );
}

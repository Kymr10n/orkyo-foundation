import { useMutation, useQuery } from "@tanstack/react-query";
import {
  changePassword,
  enableMfa,
  getMfaStatus,
  getPasskeys,
  getSecurityInfo,
  getSessions,
  logoutAllSessions,
  removeMfa,
  removePasskey,
  renamePasskey,
  revokeSession,
} from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useInvalidateKeys } from "@foundation/src/hooks/useInvalidateKeys";

/** One entry of the current user's active-session list. */
export type SessionItem = Awaited<ReturnType<typeof getSessions>>[number];

export const useSecurityInfo = () =>
  useQuery({
    queryKey: qk.security.info(),
    queryFn: getSecurityInfo,
  });

export const useMfaStatus = () =>
  useQuery({
    queryKey: qk.mfa.status(),
    queryFn: getMfaStatus,
  });

export const useRemoveMfa = () =>
  useMutation({
    mutationFn: removeMfa,
    meta: { invalidates: [qk.mfa.status()] },
  });

export const useEnableMfa = () =>
  useMutation({
    mutationFn: enableMfa,
    meta: { invalidates: [qk.mfa.status()] },
  });

export const usePasskeys = () =>
  useQuery({
    queryKey: qk.passkeys.all(),
    queryFn: getPasskeys,
  });

/** Refetches the passkey list after Keycloak's "add a passkey" step returns to the account page. */
export const useInvalidatePasskeys = () => useInvalidateKeys(qk.passkeys.all());

export const useRenamePasskey = () =>
  useMutation({
    mutationFn: renamePasskey,
    meta: {
      successMessage: "Passkey renamed",
      errorMessage: "Failed to rename passkey",
      invalidates: [qk.passkeys.all()],
    },
  });

export const useRemovePasskey = () =>
  useMutation({
    mutationFn: removePasskey,
    meta: { invalidates: [qk.passkeys.all()] },
  });

export const useChangePassword = () =>
  useMutation({
    mutationFn: changePassword,
  });

export const useSessions = () =>
  useQuery({
    queryKey: qk.sessions.all(),
    queryFn: getSessions,
  });

export const useRevokeSession = () =>
  useMutation({
    mutationFn: revokeSession,
    meta: {
      successMessage: "Session signed out",
      errorMessage: "Failed to sign out session",
      invalidates: [qk.sessions.all()],
    },
  });

export const useLogoutAllSessions = () =>
  useMutation({
    mutationFn: logoutAllSessions,
    meta: {
      successMessage: "Signed out everywhere",
      errorMessage: "Failed to sign out everywhere",
    },
  });

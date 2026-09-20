import { useMutation, useQuery } from "@tanstack/react-query";
import {
  changePassword,
  enableMfa,
  getMfaStatus,
  getSecurityInfo,
  getSessions,
  logoutAllSessions,
  removeMfa,
  revokeSession,
} from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";

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

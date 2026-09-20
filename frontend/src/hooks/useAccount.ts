import { useCallback, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getUserProfile,
  requestEmailChange,
  updateUserProfile,
} from "@foundation/src/lib/api/security-api";
import {
  getTenantMemberships,
  type TenantMembership,
} from "@foundation/src/lib/api/tenant-account-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { logger } from "@foundation/src/lib/core/logger";

export const useUserProfile = () =>
  useQuery({
    queryKey: qk.userProfile.all(),
    queryFn: getUserProfile,
  });

export const useUpdateUserProfile = () =>
  useMutation({
    mutationFn: updateUserProfile,
    meta: { invalidates: [qk.userProfile.all()] },
  });

export const useRequestEmailChange = () =>
  useMutation({
    mutationFn: (email: string) => requestEmailChange(email),
    meta: {
      successMessage: (_data: unknown, email: unknown) =>
        `Confirmation email sent to ${email as string}. Check your inbox.`,
      errorMessage: "Failed to request email change",
    },
  });

/**
 * Refetches the profile without transitioning the auth machine. `refresh()` would
 * send it back to `initializing`, which unmounts TenantApp (and its Toaster)
 * before Sonner can display a pending toast.
 */
export const useInvalidateUserProfile = () => {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: qk.userProfile.all() }),
    [queryClient],
  );
};

/**
 * The caller's tenant memberships. Loaded manually by design on this operator
 * surface — see docs/dialog-feedback.md. A 401 signals the auth machine instead
 * of surfacing an error, and `reload` re-reads the list after leave/delete.
 */
export const useTenantMemberships = () => {
  const { send } = useAuth();
  const [memberships, setMemberships] = useState<TenantMembership[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const data = await getTenantMemberships();
      setMemberships(data);
    } catch (err) {
      logger.error("Failed to load memberships:", err);
      // If unauthorized, signal the machine — it handles the redirect
      if (err instanceof Error && err.message.includes("401")) {
        send({ type: "UNAUTHORIZED" });
        return;
      }
      setError(
        err instanceof Error ? err.message : "Failed to load memberships",
      );
    } finally {
      setLoading(false);
    }
  }, [send]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    reload();
  }, [reload]);

  return { memberships, loading, error, setError, reload };
};

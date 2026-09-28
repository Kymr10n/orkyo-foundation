import { useCallback, useEffect, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getUserProfile,
  requestEmailChange,
  updateUserProfile,
} from "@foundation/src/lib/api/security-api";
import {
  deleteTenant,
  getTenantMemberships,
  leaveTenant,
  type AccountMembership,
} from "@foundation/src/lib/api/tenant-account-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { logger } from "@foundation/src/lib/core/logger";
import { useInvalidateKeys } from "@foundation/src/hooks/useInvalidateKeys";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

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
      // The email editor stays open on failure and shows the message inline; one surface.
      suppressErrorToast: true,
    },
  });

/**
 * Refetches the profile without transitioning the auth machine. `refresh()` would
 * send it back to `initializing`, which unmounts TenantApp (and its Toaster)
 * before Sonner can display a pending toast.
 */
export const useInvalidateUserProfile = () => useInvalidateKeys(qk.userProfile.all());

/**
 * The caller's tenant memberships, loaded on mount and re-read by `reload` rather than
 * held in react-query. A 401 never reaches here as an error to
 * show: `handleApiError` has already sent the browser to login. `reload` re-reads the
 * list after leave/delete.
 */
export const useTenantMemberships = () => {
  const [memberships, setMemberships] = useState<AccountMembership[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const data = await getTenantMemberships();
      setMemberships(data);
      setError(null);
    } catch (err) {
      logger.error("Failed to load memberships:", err);
      setError(errorMessage(err, "Failed to load memberships"));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void reload();
  }, [reload]);

  return { memberships, loading, error, setError, reload };
};

/** Leave one organization. No `meta`: the account page shows a failure inline. */
export const useLeaveTenant = () =>
  useMutation({ mutationFn: (tenantId: string) => leaveTenant(tenantId) });

/** Start deleting one organization. No `meta`: the account page shows a failure inline. */
export const useDeleteTenant = () =>
  useMutation({ mutationFn: (tenantId: string) => deleteTenant(tenantId) });

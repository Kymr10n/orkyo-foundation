import { useMutation, useQuery } from "@tanstack/react-query";
import {
  getNotificationPreferences,
  updateNotificationPreferences,
} from "@foundation/src/lib/api/security-api";
import { qk } from "@foundation/src/lib/api/query-keys";

export const useNotificationPreferences = () =>
  useQuery({
    queryKey: qk.notificationPreferences.all(),
    queryFn: getNotificationPreferences,
  });

export const useUpdateNotificationPreferences = () =>
  useMutation({
    // The switch is "Receive announcement emails" (on = opted in), so opt-out is the inverse.
    mutationFn: (receiveEmails: boolean) =>
      updateNotificationPreferences(!receiveEmails),
    meta: {
      successMessage: "Email preferences updated",
      errorMessage: "Failed to update email preferences",
      invalidates: [qk.notificationPreferences.all()],
    },
  });

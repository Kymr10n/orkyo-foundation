import { apiGet, apiPut } from "@foundation/src/lib/core/api-client";
import { API_PATHS } from "@foundation/src/lib/core/api-paths";
import { useMutation, useQuery } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";

export interface UserPreferences {
  spaceOrder?: string[];
  [key: string]: string[] | string | number | boolean | undefined; // Allow other preferences
}

const fetchPreferences = () => apiGet<UserPreferences>(API_PATHS.PREFERENCES);

const updatePreferences = (preferences: UserPreferences) =>
  apiPut<void>(API_PATHS.PREFERENCES, preferences);

export function usePreferences() {
  return useQuery({
    queryKey: qk.preferences.all(),
    queryFn: fetchPreferences,
  });
}

export function useUpdatePreferences() {
  return useMutation({
    mutationFn: updatePreferences,
    meta: {
      invalidates: [qk.preferences.all()],
    },
  });
}

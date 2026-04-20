import { apiGet, apiPut } from "@/lib/core/api-client";
import { API_PATHS } from "@/lib/core/api-paths";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export interface UserPreferences {
  spaceOrder?: string[];
  [key: string]: string[] | string | number | boolean | undefined; // Allow other preferences
}

const fetchPreferences = () => apiGet<UserPreferences>(API_PATHS.PREFERENCES);

const updatePreferences = (preferences: UserPreferences) => 
  apiPut<void>(API_PATHS.PREFERENCES, preferences);

export function usePreferences() {
  return useQuery({
    queryKey: ["preferences"],
    queryFn: fetchPreferences,
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: 1,
  });
}

export function useUpdatePreferences() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: updatePreferences,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["preferences"] });
    },
  });
}

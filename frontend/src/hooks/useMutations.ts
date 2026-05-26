import { useMutation, useQueryClient, useQuery as useTanstackQuery, type UseQueryOptions } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Generic factory for creating CRUD mutation hooks with automatic cache invalidation
 * and optional success/error toasts.
 *
 * Pass `entityLabel` (e.g. "Site", "Criterion") to opt into toast feedback. When
 * provided, the hooks fire `${entityLabel} created/updated/deleted` on success
 * and `Failed to {create|update|delete} ${entityLabel}` on error.
 */

interface MutationConfig<TData, TCreate, TUpdate, TParams> {
  queryKey: (params?: TParams) => readonly unknown[];
  queryFn: (params?: TParams) => Promise<TData[]>;
  createFn?: (data: TCreate, params?: TParams) => Promise<TData>;
  updateFn?: (id: string, data: TUpdate, params?: TParams) => Promise<TData>;
  deleteFn?: (id: string, params?: TParams) => Promise<void>;
  invalidateKeys?: (params?: TParams) => unknown[][];
  /** When set, fires success/error toasts using this label (singular form). */
  entityLabel?: string;
}

function errorMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  return String(err);
}

export function createCrudHooks<TData, TCreate = Partial<TData>, TUpdate = Partial<TData>, TParams = undefined>(
  config: MutationConfig<TData, TCreate, TUpdate, TParams>
) {
  // Invalidate by prefix (exact: false) so a mutation against e.g. ['sites']
  // also invalidates ['sites', filter] variants. The previous exact-match
  // behaviour silently missed param-bearing cache entries.
  const invalidateAll = (queryClient: ReturnType<typeof useQueryClient>, params?: TParams) => {
    const keysToInvalidate = [
      config.queryKey(params),
      ...(config.invalidateKeys?.(params) || []),
    ];
    keysToInvalidate.forEach((key) => {
      queryClient.invalidateQueries({ queryKey: key, exact: false });
    });
  };

  // Query hook
  const useQuery = (params?: TParams, options?: Partial<UseQueryOptions<TData[]>>) => {
    return useTanstackQuery({
      queryKey: config.queryKey(params),
      queryFn: () => config.queryFn(params),
      enabled: params !== null && params !== undefined,
      staleTime: 60 * 1000,
      ...options,
    });
  };

  // Create mutation
  const useCreate = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: (data: TCreate) => config.createFn!(data, params),
      onSuccess: () => {
        invalidateAll(queryClient, params);
        if (config.entityLabel) toast.success(`${config.entityLabel} created`);
      },
      onError: (err) => {
        if (config.entityLabel) {
          toast.error(`Failed to create ${config.entityLabel.toLowerCase()}`, {
            description: errorMessage(err),
          });
        }
      },
    });
  };

  // Update mutation
  const useUpdate = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: ({ id, data }: { id: string; data: TUpdate }) =>
        config.updateFn!(id, data, params),
      onSuccess: () => {
        invalidateAll(queryClient, params);
        if (config.entityLabel) toast.success(`${config.entityLabel} updated`);
      },
      onError: (err) => {
        if (config.entityLabel) {
          toast.error(`Failed to update ${config.entityLabel.toLowerCase()}`, {
            description: errorMessage(err),
          });
        }
      },
    });
  };

  // Delete mutation
  const useDelete = (params?: TParams) => {
    const queryClient = useQueryClient();

    return useMutation({
      mutationFn: (id: string) => config.deleteFn!(id, params),
      onSuccess: () => {
        invalidateAll(queryClient, params);
        if (config.entityLabel) toast.success(`${config.entityLabel} deleted`);
      },
      onError: (err) => {
        if (config.entityLabel) {
          toast.error(`Failed to delete ${config.entityLabel.toLowerCase()}`, {
            description: errorMessage(err),
          });
        }
      },
    });
  };

  return {
    useQuery,
    useCreate,
    useUpdate,
    useDelete,
  };
}
